using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldRevitPreviewService
{
    private const string OwnedComment = "TrueBIM IsoFieldRebar Preview";
    private const double MinimumSegmentLengthFeet = 0.001;
    private readonly IsoFieldCoordinateMapper coordinateMapper = new();
    private readonly ITrueBimLogger logger;

    public IsoFieldRevitPreviewService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IsoFieldRevitPreviewResult Show(
        UIDocument uiDocument,
        IsoFieldRecognitionResult recognitionResult,
        IReadOnlyCollection<ElementId> currentPreviewIds,
        IsoFieldCalibration calibration)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (recognitionResult is null)
        {
            throw new ArgumentNullException(nameof(recognitionResult));
        }

        coordinateMapper.Validate(calibration);

        Document document = uiDocument.Document;
        View activeView = uiDocument.ActiveView;
        EnsureViewSupportsDetailPreview(activeView);

        IReadOnlyList<ElementId> idsToDelete = CollectPreviewIds(document, activeView, currentPreviewIds);
        logger.Info($"IsoField Revit preview service started. View='{activeView.Name}'; Polylines={recognitionResult.Polylines.Count}; ExistingPreviewIds={idsToDelete.Count}; MillimetersPerPixel={calibration.MillimetersPerPixel}.");
        if (recognitionResult.Polylines.Count == 0)
        {
            int deletedOnly = DeletePreviewElements(document, activeView, idsToDelete);
            logger.Info($"IsoField Revit preview service finished without polylines. Deleted={deletedOnly}; View='{activeView.Name}'.");
            return new IsoFieldRevitPreviewResult(
                0,
                deletedOnly,
                Array.Empty<ElementId>(),
                "Нет контуров для предпросмотра в Revit.");
        }

        PreviewFrame frame = CreatePreviewFrame(uiDocument, activeView);
        List<ElementId> createdIds = new();
        int deletedCount = 0;

        using Transaction transaction = new(document, "TrueBIM: предпросмотр изополей");
        transaction.Start();

        try
        {
            deletedCount = DeletePreviewElementsWithoutTransaction(document, activeView, idsToDelete);
            foreach (IsoFieldPolyline polyline in recognitionResult.Polylines)
            {
                for (int index = 0; index < polyline.Points.Count - 1; index++)
                {
                    XYZ start = ToRevitPoint(frame, calibration, polyline.Points[index]);
                    XYZ end = ToRevitPoint(frame, calibration, polyline.Points[index + 1]);
                    if (start.DistanceTo(end) < MinimumSegmentLengthFeet)
                    {
                        continue;
                    }

                    DetailCurve curve = document.Create.NewDetailCurve(activeView, Line.CreateBound(start, end));
                    MarkOwnedPreviewCurve(curve);
                    createdIds.Add(curve.Id);
                }
            }

            transaction.Commit();
        }
        catch (Exception exception)
        {
            transaction.RollBack();
            logger.Error($"IsoField Revit preview transaction rolled back. View='{activeView.Name}'.", exception);
            throw;
        }

        logger.Info($"IsoField Revit preview updated. Created={createdIds.Count}; Deleted={deletedCount}; View='{activeView.Name}'; MillimetersPerPixel={calibration.MillimetersPerPixel}.");
        return new IsoFieldRevitPreviewResult(
            createdIds.Count,
            deletedCount,
            createdIds,
            createdIds.Count == 0
                ? "Контуры прочитаны, но подходящих сегментов для калиброванного предпросмотра в Revit не найдено."
                : $"Калиброванные линии предпросмотра в Revit созданы: {createdIds.Count}.");
    }

    public IsoFieldRevitPreviewResult Clear(
        UIDocument uiDocument,
        IReadOnlyCollection<ElementId> currentPreviewIds)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        Document document = uiDocument.Document;
        View activeView = uiDocument.ActiveView;
        IReadOnlyList<ElementId> idsToDelete = CollectPreviewIds(document, activeView, currentPreviewIds);
        logger.Info($"IsoField Revit preview clear service started. View='{activeView.Name}'; ExistingPreviewIds={idsToDelete.Count}.");
        int deletedCount = DeletePreviewElements(document, activeView, idsToDelete);

        logger.Info($"IsoField Revit preview cleared. Deleted={deletedCount}; View='{activeView.Name}'.");
        return new IsoFieldRevitPreviewResult(
            0,
            deletedCount,
            Array.Empty<ElementId>(),
            deletedCount == 0
                ? "Линии предпросмотра в Revit не найдены."
                : $"Линии предпросмотра в Revit удалены: {deletedCount}.");
    }

    private static int DeletePreviewElements(Document document, View activeView, IReadOnlyList<ElementId> idsToDelete)
    {
        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        using Transaction transaction = new(document, "TrueBIM: очистить предпросмотр изополей");
        transaction.Start();

        try
        {
            int deletedCount = DeletePreviewElementsWithoutTransaction(document, activeView, idsToDelete);
            transaction.Commit();
            return deletedCount;
        }
        catch
        {
            transaction.RollBack();
            throw;
        }
    }

    private static int DeletePreviewElementsWithoutTransaction(
        Document document,
        View activeView,
        IReadOnlyList<ElementId> idsToDelete)
    {
        int deletedCount = 0;
        foreach (ElementId elementId in idsToDelete)
        {
            Element? element = document.GetElement(elementId);
            if (element is null || element.OwnerViewId != activeView.Id)
            {
                continue;
            }

            document.Delete(elementId);
            deletedCount++;
        }

        return deletedCount;
    }

    private static IReadOnlyList<ElementId> CollectPreviewIds(
        Document document,
        View activeView,
        IReadOnlyCollection<ElementId>? currentPreviewIds)
    {
        HashSet<ElementId> ids = new(currentPreviewIds ?? Array.Empty<ElementId>());
        foreach (CurveElement curve in new FilteredElementCollector(document, activeView.Id)
            .OfClass(typeof(CurveElement))
            .Cast<CurveElement>())
        {
            if (IsOwnedPreviewCurve(curve))
            {
                ids.Add(curve.Id);
            }
        }

        return ids.ToArray();
    }

    private static bool IsOwnedPreviewCurve(CurveElement curve)
    {
        Parameter? parameter = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        return string.Equals(parameter?.AsString(), OwnedComment, StringComparison.Ordinal);
    }

    private static void MarkOwnedPreviewCurve(CurveElement curve)
    {
        Parameter? parameter = curve.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        if (parameter is not null && !parameter.IsReadOnly)
        {
            parameter.Set(OwnedComment);
        }
    }

    private static void EnsureViewSupportsDetailPreview(View view)
    {
        if (view.IsTemplate || !IsDetailPreviewViewType(view.ViewType))
        {
            throw new InvalidOperationException("Активный вид не поддерживает линии предпросмотра изополей.");
        }
    }

    private static bool IsDetailPreviewViewType(ViewType viewType)
    {
        return viewType is ViewType.FloorPlan
            or ViewType.CeilingPlan
            or ViewType.EngineeringPlan
            or ViewType.AreaPlan
            or ViewType.Section
            or ViewType.Elevation
            or ViewType.Detail
            or ViewType.DraftingView
            or ViewType.Legend;
    }

    private PreviewFrame CreatePreviewFrame(UIDocument uiDocument, View activeView)
    {
        return new PreviewFrame(
            ResolveViewCenter(uiDocument, activeView),
            activeView.RightDirection.Normalize(),
            activeView.UpDirection.Normalize());
    }

    private static XYZ ResolveViewCenter(UIDocument uiDocument, View activeView)
    {
        UIView? uiView = uiDocument.GetOpenUIViews()
            .FirstOrDefault(view => view.ViewId == activeView.Id);
        if (uiView is null)
        {
            return activeView.Origin;
        }

        IList<XYZ> corners = uiView.GetZoomCorners();
        return corners.Count >= 2
            ? (corners[0] + corners[1]) / 2
            : activeView.Origin;
    }

    private XYZ ToRevitPoint(PreviewFrame frame, IsoFieldCalibration calibration, IsoFieldPoint point)
    {
        IsoFieldPoint mappedPoint = coordinateMapper.MapToRevitPlaneFeet(point, calibration);
        return frame.Center + (frame.Right * mappedPoint.X) + (frame.Up * mappedPoint.Y);
    }

    private sealed record PreviewFrame(XYZ Center, XYZ Right, XYZ Up);
}
