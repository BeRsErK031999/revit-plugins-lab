using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using RevitColor = Autodesk.Revit.DB.Color;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashViewNavigator
{
    private const string ViewName = "BIM_Clash_Report_3D";
    private readonly ITrueBimLogger logger;

    public ClashViewNavigator(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ClashNavigationResult Focus(UIDocument uiDocument, Document document, ClashItem item, ClashReportProfile profile)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(item, nameof(item));
        Guard.NotNull(profile, nameof(profile));

        List<Element> elements = ResolveElements(document, item.GetResolvedElementIds());
        BoundingBoxXYZ? sectionBox = BuildSectionBox(elements, item);
        if (sectionBox is null)
        {
            return new ClashNavigationResult(false, "Нет найденных элементов или координат для построения section box.", string.Empty, 0);
        }

        View3D? view = null;
        using (Transaction transaction = new(document, "TrueBIM: показать коллизию"))
        {
            transaction.Start();
            view = FindOrCreateView(document);
            view.IsSectionBoxActive = true;
            view.SetSectionBox(sectionBox);

            if (profile.HighlightOnNavigate)
            {
                ApplyOverrides(view, elements);
            }

            transaction.Commit();
        }

        try
        {
            uiDocument.ActiveView = view;
            uiDocument.Selection.SetElementIds(elements.Select(element => element.Id).ToList());
        }
        catch (Exception exception) when (exception is InvalidOperationException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            logger.Warning($"Failed to activate clash report view '{view.Name}': {exception.Message}");
            return new ClashNavigationResult(true, $"Section box обновлен, но Revit не переключил активный вид: {exception.Message}", view.Name, elements.Count);
        }

        logger.Info($"Clash '{item.ClashId}' focused in '{view.Name}' with {elements.Count} resolved elements.");
        return new ClashNavigationResult(true, "Открыт 3D-вид с section box вокруг коллизии.", view.Name, elements.Count);
    }

    private static View3D FindOrCreateView(Document document)
    {
        View3D? existing = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate && string.Equals(view.Name, ViewName, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        ViewFamilyType viewFamilyType = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(type => type.ViewFamily == ViewFamily.ThreeDimensional);
        View3D view3D = View3D.CreateIsometric(document, viewFamilyType.Id);
        view3D.Name = ViewName;
        return view3D;
    }

    private static List<Element> ResolveElements(Document document, IReadOnlyList<long> elementIds)
    {
        List<Element> elements = [];
        foreach (long elementId in elementIds)
        {
            Element? element = document.GetElement(RevitElementIds.Create(elementId));
            if (element is not null)
            {
                elements.Add(element);
            }
        }

        return elements;
    }

    private static BoundingBoxXYZ? BuildSectionBox(IReadOnlyList<Element> elements, ClashItem item)
    {
        if (item.Bounds is { } bounds)
        {
            double boundsMinX = bounds.MinX;
            double boundsMinY = bounds.MinY;
            double boundsMinZ = bounds.MinZ;
            double boundsMaxX = bounds.MaxX;
            double boundsMaxY = bounds.MaxY;
            double boundsMaxZ = bounds.MaxZ;
            EnsureMinimumSize(ref boundsMinX, ref boundsMaxX, 1.0);
            EnsureMinimumSize(ref boundsMinY, ref boundsMaxY, 1.0);
            EnsureMinimumSize(ref boundsMinZ, ref boundsMaxZ, 1.0);

            return new BoundingBoxXYZ
            {
                Min = new XYZ(boundsMinX, boundsMinY, boundsMinZ),
                Max = new XYZ(boundsMaxX, boundsMaxY, boundsMaxZ)
            };
        }

        List<XYZ> points = [];
        foreach (Element element in elements)
        {
            BoundingBoxXYZ? boundingBox = element.get_BoundingBox(null);
            if (boundingBox is null)
            {
                continue;
            }

            points.Add(boundingBox.Min);
            points.Add(boundingBox.Max);
        }

        if (item.HasPoint)
        {
            points.Add(new XYZ(item.X!.Value, item.Y!.Value, item.Z!.Value));
        }

        if (points.Count == 0)
        {
            return null;
        }

        double minX = points.Min(point => point.X);
        double minY = points.Min(point => point.Y);
        double minZ = points.Min(point => point.Z);
        double maxX = points.Max(point => point.X);
        double maxY = points.Max(point => point.Y);
        double maxZ = points.Max(point => point.Z);
        EnsureMinimumSize(ref minX, ref maxX, 1.0);
        EnsureMinimumSize(ref minY, ref maxY, 1.0);
        EnsureMinimumSize(ref minZ, ref maxZ, 1.0);

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ)
        };
    }

    private static void ApplyOverrides(View3D view, IReadOnlyList<Element> elements)
    {
        OverrideGraphicSettings first = CreateOverride(new RevitColor(255, 60, 40));
        OverrideGraphicSettings second = CreateOverride(new RevitColor(30, 120, 255));

        for (int index = 0; index < elements.Count; index++)
        {
            view.SetElementOverrides(elements[index].Id, index == 0 ? first : second);
        }
    }

    private static OverrideGraphicSettings CreateOverride(RevitColor color)
    {
        OverrideGraphicSettings settings = new();
        settings.SetProjectionLineColor(color);
        settings.SetProjectionLineWeight(6);
        return settings;
    }

    private static void EnsureMinimumSize(ref double min, ref double max, double minimum)
    {
        if (max - min >= minimum)
        {
            return;
        }

        double center = (min + max) * 0.5;
        min = center - minimum * 0.5;
        max = center + minimum * 0.5;
    }
}
