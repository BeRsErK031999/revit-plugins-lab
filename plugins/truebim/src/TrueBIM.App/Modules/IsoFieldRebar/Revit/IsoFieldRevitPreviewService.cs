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
    private readonly ITrueBimLogger logger;

    public IsoFieldRevitPreviewService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IsoFieldRevitPreviewResult Show(
        UIDocument uiDocument,
        IsoFieldSlabBindingAnalysis bindingAnalysis,
        IReadOnlyCollection<ElementId> currentPreviewIds,
        bool includeZoneHoles = true)
    {
        if (uiDocument is null)
        {
            throw new ArgumentNullException(nameof(uiDocument));
        }

        if (bindingAnalysis is null)
        {
            throw new ArgumentNullException(nameof(bindingAnalysis));
        }

        Document document = uiDocument.Document;
        View activeView = uiDocument.ActiveView;
        EnsureViewSupportsDetailPreview(activeView);
        EnsureHostIsFaceOn(activeView, bindingAnalysis.HostGeometry);

        IReadOnlyList<ElementId> idsToDelete = CollectPreviewIds(document, activeView, currentPreviewIds);
        IReadOnlyList<IReadOnlyList<IsoFieldPoint>> previewLoops = CollectPreviewLoops(
            bindingAnalysis,
            includeZoneHoles);
        logger.Info(
            $"IsoField bound Revit preview service started. View='{activeView.Name}'; "
            + $"Zones={bindingAnalysis.ClippedZones.Count}; Loops={previewLoops.Count}; "
            + $"ExistingPreviewIds={idsToDelete.Count}.");
        if (previewLoops.Count == 0)
        {
            int deletedOnly = DeletePreviewElements(document, activeView, idsToDelete);
            logger.Info($"IsoField bound Revit preview service finished without loops. Deleted={deletedOnly}; View='{activeView.Name}'.");
            return new IsoFieldRevitPreviewResult(
                0,
                deletedOnly,
                Array.Empty<ElementId>(),
                "После привязки и обрезки не осталось контуров, которые можно показать на выбранной конструкции.");
        }

        List<ElementId> createdIds = new();
        int deletedCount = 0;

        using Transaction transaction = new(document, "TrueBIM: показать вспомогательные линии");
        transaction.Start();

        try
        {
            deletedCount = DeletePreviewElementsWithoutTransaction(document, activeView, idsToDelete);
            foreach (IReadOnlyList<IsoFieldPoint> loop in previewLoops)
            {
                for (int index = 0; index < loop.Count - 1; index++)
                {
                    XYZ start = ToRevitPoint(activeView, bindingAnalysis.HostGeometry, loop[index]);
                    XYZ end = ToRevitPoint(activeView, bindingAnalysis.HostGeometry, loop[index + 1]);
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

        logger.Info($"IsoField bound Revit preview updated. Created={createdIds.Count}; Deleted={deletedCount}; View='{activeView.Name}'.");
        return new IsoFieldRevitPreviewResult(
            createdIds.Count,
            deletedCount,
            createdIds,
            createdIds.Count == 0
                ? "Привязанные контуры найдены, но вспомогательные линии для текущего вида создать не удалось."
                : $"На выбранной конструкции показаны привязанные и обрезанные контуры: {createdIds.Count} линий. Когда они станут не нужны, нажмите «Удалить линии с вида».");
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
                ? "Вспомогательные линии этого модуля на текущем виде не найдены."
                : $"Вспомогательные линии удалены с текущего вида: {deletedCount}.");
    }

    private static int DeletePreviewElements(Document document, View activeView, IReadOnlyList<ElementId> idsToDelete)
    {
        if (idsToDelete.Count == 0)
        {
            return 0;
        }

        using Transaction transaction = new(document, "TrueBIM: удалить вспомогательные линии");
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
            throw new InvalidOperationException("На текущем виде нельзя создать вспомогательные линии. Откройте план, разрез, фасад или чертёжный вид.");
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

    private static void EnsureHostIsFaceOn(View activeView, IsoFieldHostGeometry hostGeometry)
    {
        XYZ viewNormal = activeView.ViewDirection.Normalize();
        XYZ hostNormal = ToXyz(hostGeometry.Normal).Normalize();
        if (Math.Abs(viewNormal.DotProduct(hostNormal)) < 0.99)
        {
            throw new InvalidOperationException(
                "Текущий вид смотрит на выбранную конструкцию сбоку. Откройте план для плиты или фасад/разрез, перпендикулярный поверхности стены, и повторите показ.");
        }
    }

    private static IReadOnlyList<IReadOnlyList<IsoFieldPoint>> CollectPreviewLoops(
        IsoFieldSlabBindingAnalysis bindingAnalysis,
        bool includeZoneHoles)
    {
        List<IReadOnlyList<IsoFieldPoint>> loops = new();
        foreach (IsoFieldPolygonRegion region in bindingAnalysis.ClippedZones
            .Where(zone => !zone.IsEmpty)
            .SelectMany(zone => zone.Regions))
        {
            loops.Add(region.OuterBoundaryFeet);
            if (includeZoneHoles)
            {
                loops.AddRange(region.HoleBoundariesFeet);
            }
        }

        return loops;
    }

    private static XYZ ToRevitPoint(
        View activeView,
        IsoFieldHostGeometry hostGeometry,
        IsoFieldPoint localPoint)
    {
        XYZ origin = ToXyz(hostGeometry.OriginFeet);
        XYZ axisX = ToXyz(hostGeometry.AxisX);
        XYZ axisY = ToXyz(hostGeometry.AxisY);
        XYZ worldPoint = origin + (axisX * localPoint.X) + (axisY * localPoint.Y);
        XYZ viewNormal = activeView.ViewDirection.Normalize();
        double distanceToViewPlane = (worldPoint - activeView.Origin).DotProduct(viewNormal);
        return worldPoint - (viewNormal * distanceToViewPlane);
    }

    private static XYZ ToXyz(IsoFieldRebarPoint3D point)
    {
        return new XYZ(point.XFeet, point.YFeet, point.ZFeet);
    }
}
