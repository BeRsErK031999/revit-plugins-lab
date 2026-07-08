using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public static class MepDimensionReferenceResolver
{
    public static bool TryResolve(
        Element element,
        View activeView,
        bool allowElementReferenceFallback,
        out Reference? reference,
        out string source)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(activeView, nameof(activeView));

        if (element.Location is LocationCurve locationCurve && locationCurve.Curve.Reference is not null)
        {
            reference = locationCurve.Curve.Reference;
            source = "LocationCurve";
            return true;
        }

        Options options = new()
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = false,
            View = activeView
        };

        GeometryElement? geometry = element.get_Geometry(options);
        Reference? geometryReference = FindGeometryCurveReference(geometry);
        if (geometryReference is not null)
        {
            reference = geometryReference;
            source = "GeometryCurve";
            return true;
        }

        if (allowElementReferenceFallback)
        {
            reference = new Reference(element);
            source = "Element";
            return true;
        }

        reference = null;
        source = "Reference не найден.";
        return false;
    }

    private static Reference? FindGeometryCurveReference(GeometryElement? geometry)
    {
        if (geometry is null)
        {
            return null;
        }

        foreach (GeometryObject geometryObject in geometry)
        {
            if (geometryObject is Curve curve && curve.Reference is not null)
            {
                return curve.Reference;
            }

            if (geometryObject is GeometryInstance instance)
            {
                Reference? nested = FindGeometryCurveReference(instance.GetInstanceGeometry());
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
