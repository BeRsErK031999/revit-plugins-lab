using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewBoundsResolver
{
    public static OpeningViewBoundsResult? Resolve(Element element, View activeView)
    {
        Guard.NotNull(element, nameof(element));
        Guard.NotNull(activeView, nameof(activeView));

        OpeningViewBounds? modelBounds = ToModelBounds(element.get_BoundingBox(null));
        if (modelBounds is not null)
        {
            return Select(modelBounds, null);
        }

        OpeningViewBounds? viewSpecificBounds = ToModelBounds(element.get_BoundingBox(activeView));
        return Select(null, viewSpecificBounds);
    }

    public static OpeningViewBoundsResult? Select(
        OpeningViewBounds? modelBounds,
        OpeningViewBounds? viewSpecificBounds)
    {
        if (modelBounds is not null)
        {
            return new OpeningViewBoundsResult(modelBounds, usedViewSpecificFallback: false);
        }

        return viewSpecificBounds is null
            ? null
            : new OpeningViewBoundsResult(viewSpecificBounds, usedViewSpecificFallback: true);
    }

    private static OpeningViewBounds? ToModelBounds(BoundingBoxXYZ? boundingBox)
    {
        if (boundingBox is null)
        {
            return null;
        }

        Transform transform = boundingBox.Transform;
        IReadOnlyList<XYZ> points = GetCorners(boundingBox.Min, boundingBox.Max)
            .Select(transform.OfPoint)
            .ToList();
        if (points.Any(point => !IsFinite(point.X) || !IsFinite(point.Y) || !IsFinite(point.Z)))
        {
            return null;
        }

        return new OpeningViewBounds(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Min(point => point.Z),
            points.Max(point => point.X),
            points.Max(point => point.Y),
            points.Max(point => point.Z));
    }

    private static IReadOnlyList<XYZ> GetCorners(XYZ min, XYZ max)
    {
        return
        [
            new XYZ(min.X, min.Y, min.Z),
            new XYZ(max.X, min.Y, min.Z),
            new XYZ(min.X, max.Y, min.Z),
            new XYZ(max.X, max.Y, min.Z),
            new XYZ(min.X, min.Y, max.Z),
            new XYZ(max.X, min.Y, max.Z),
            new XYZ(min.X, max.Y, max.Z),
            new XYZ(max.X, max.Y, max.Z)
        ];
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
