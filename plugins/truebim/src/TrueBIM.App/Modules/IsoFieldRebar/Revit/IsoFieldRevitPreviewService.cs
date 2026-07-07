using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed class IsoFieldRevitPreviewService
{
    private const string OwnedComment = "TrueBIM IsoFieldRebar Preview";
    private const double DefaultPreviewSpanFeet = 20.0;
    private const double MinimumSegmentLengthFeet = 0.001;
    private readonly ITrueBimLogger logger;

    public IsoFieldRevitPreviewService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IsoFieldRevitPreviewResult Show(
        UIDocument uiDocument,
        IsoFieldRecognitionResult recognitionResult,
        IReadOnlyCollection<ElementId> currentPreviewIds)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (recognitionResult is null)
        {
            throw new ArgumentNullException(nameof(recognitionResult));
        }

        Document document = uiDocument.Document;
        View activeView = uiDocument.ActiveView;
        EnsureViewSupportsDetailPreview(activeView);

        IReadOnlyList<ElementId> idsToDelete = CollectPreviewIds(document, activeView, currentPreviewIds);
        if (recognitionResult.Polylines.Count == 0)
        {
            int deletedOnly = DeletePreviewElements(document, activeView, idsToDelete);
            return new IsoFieldRevitPreviewResult(
                0,
                deletedOnly,
                Array.Empty<ElementId>(),
                "Нет контуров для предпросмотра в Revit.");
        }

        PreviewFrame frame = CreatePreviewFrame(uiDocument, activeView, recognitionResult);
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
                    XYZ start = frame.ToRevitPoint(polyline.Points[index]);
                    XYZ end = frame.ToRevitPoint(polyline.Points[index + 1]);
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
        catch
        {
            transaction.RollBack();
            throw;
        }

        logger.Info($"IsoField Revit preview updated. Created={createdIds.Count}; Deleted={deletedCount}; View='{activeView.Name}'.");
        return new IsoFieldRevitPreviewResult(
            createdIds.Count,
            deletedCount,
            createdIds,
            createdIds.Count == 0
                ? "Контуры прочитаны, но подходящих сегментов для предпросмотра в Revit не найдено."
                : $"Линии предпросмотра в Revit созданы: {createdIds.Count}.");
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

    private static PreviewFrame CreatePreviewFrame(
        UIDocument uiDocument,
        View activeView,
        IsoFieldRecognitionResult recognitionResult)
    {
        IReadOnlyList<IsoFieldPoint> points = recognitionResult.Polylines
            .SelectMany(polyline => polyline.Points)
            .ToArray();
        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        double sourceWidth = Math.Max(maxX - minX, 1);
        double sourceHeight = Math.Max(maxY - minY, 1);
        double sourceCenterX = (minX + maxX) / 2;
        double sourceCenterY = (minY + maxY) / 2;
        double targetSpan = ResolveTargetSpan(uiDocument, activeView);
        double scale = Math.Min(targetSpan / sourceWidth, targetSpan / sourceHeight);

        return new PreviewFrame(
            ResolveViewCenter(uiDocument, activeView),
            activeView.RightDirection.Normalize(),
            activeView.UpDirection.Normalize(),
            sourceCenterX,
            sourceCenterY,
            scale);
    }

    private static double ResolveTargetSpan(UIDocument uiDocument, View activeView)
    {
        UIView? uiView = uiDocument.GetOpenUIViews()
            .FirstOrDefault(view => view.ViewId == activeView.Id);
        if (uiView is null)
        {
            return DefaultPreviewSpanFeet;
        }

        IList<XYZ> corners = uiView.GetZoomCorners();
        if (corners.Count < 2)
        {
            return DefaultPreviewSpanFeet;
        }

        XYZ diagonal = corners[1] - corners[0];
        double width = Math.Abs(diagonal.DotProduct(activeView.RightDirection.Normalize()));
        double height = Math.Abs(diagonal.DotProduct(activeView.UpDirection.Normalize()));
        double span = Math.Min(width, height) * 0.45;
        return span > 0 ? span : DefaultPreviewSpanFeet;
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

    private sealed record PreviewFrame(
        XYZ Center,
        XYZ Right,
        XYZ Up,
        double SourceCenterX,
        double SourceCenterY,
        double Scale)
    {
        public XYZ ToRevitPoint(IsoFieldPoint point)
        {
            double x = (point.X - SourceCenterX) * Scale;
            double y = (SourceCenterY - point.Y) * Scale;
            return Center + Right * x + Up * y;
        }
    }
}
