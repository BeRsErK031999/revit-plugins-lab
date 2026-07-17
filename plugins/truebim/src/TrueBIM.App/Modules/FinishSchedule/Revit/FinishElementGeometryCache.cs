using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal sealed class FinishElementGeometryCache
{
    private const double MinimumSolidVolume = 1e-12;

    private readonly Document document;
    private readonly Options options = new()
    {
        ComputeReferences = false,
        DetailLevel = ViewDetailLevel.Fine,
        IncludeNonVisibleObjects = false
    };
    private readonly Dictionary<long, FinishElementGeometryLookup> cache = [];

    public FinishElementGeometryCache(Document document)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public FinishElementGeometryLookup Get(long elementId)
    {
        if (cache.TryGetValue(elementId, out FinishElementGeometryLookup? cached))
        {
            return cached;
        }

        Element? element = document.GetElement(RevitElementIds.Create(elementId));
        if (element is null)
        {
            FinishElementGeometryLookup missing = new(
                FinishElementGeometryLookupStatus.ElementNotFound,
                null,
                "элемент отсутствует в документе");
            cache[elementId] = missing;
            return missing;
        }

        try
        {
            List<Solid> solids = [];
            GeometryElement? geometryElement = element.get_Geometry(options);
            if (geometryElement is not null)
            {
                CollectSolids(geometryElement, solids);
            }

            FinishElementGeometryLookup result = solids.Count == 0
                ? new FinishElementGeometryLookup(
                    FinishElementGeometryLookupStatus.GeometryUnavailable,
                    null,
                    "solid-геометрия отсутствует")
                : new FinishElementGeometryLookup(
                    FinishElementGeometryLookupStatus.Success,
                    new FinishElementGeometryData(element, solids),
                    null);
            cache[elementId] = result;
            return result;
        }
        catch (Exception exception)
        {
            FinishElementGeometryLookup failed = new(
                FinishElementGeometryLookupStatus.GeometryUnavailable,
                null,
                exception.Message);
            cache[elementId] = failed;
            return failed;
        }
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        foreach (GeometryObject geometryObject in geometry)
        {
            switch (geometryObject)
            {
                case Solid solid when solid.Faces.Size > 0 && solid.Volume > MinimumSolidVolume:
                    solids.Add(solid);
                    break;
                case GeometryInstance instance:
                    GeometryElement instanceGeometry = instance.GetInstanceGeometry();
                    if (instanceGeometry is not null)
                    {
                        CollectSolids(instanceGeometry, solids);
                    }

                    break;
            }
        }
    }
}

internal sealed record FinishElementGeometryData(
    Element Element,
    IReadOnlyList<Solid> Solids);

internal enum FinishElementGeometryLookupStatus
{
    Success,
    ElementNotFound,
    GeometryUnavailable
}

internal sealed record FinishElementGeometryLookup(
    FinishElementGeometryLookupStatus Status,
    FinishElementGeometryData? Geometry,
    string? Details);
