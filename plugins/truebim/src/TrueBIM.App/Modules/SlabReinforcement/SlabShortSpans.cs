#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabShortSpans(SlabGeometry g, SlabOptions options, int direction)
{
    private Solid? centers;
    internal List<SlabStirrup> Stirrups { get; } = new();
    internal int Empty { get; private set; }

    internal void Plan(double start, double end, double row)
    {
        if (SlabLayoutRules.StirrupLength((end - start) / Mm) == 0)
        {
            Empty++;
            return;
        }
        XYZ axis = direction == 0 ? g.X : g.Y, cross = g.N.CrossProduct(axis);
        double endDiameter = options.EndType(direction).BarModelDiameter;
        double diameter = options.StirrupBar?.BarModelDiameter ?? endDiameter, radius = diameter / 2;
        var station = g.Origin + cross * row;
        // At an oblique or curved edge, the web needs cover measured perpendicular to the concrete.
        centers ??= SlabBands.CoveredSolid(g, options.EndCover * Mm + radius);
        using var ray = Line.CreateBound(station + g.N * 0.5 + axis * (start + radius), station + g.N * 0.5 + axis * (end - radius));
        using var settings = new SolidCurveIntersectionOptions();
        using var section = centers.IntersectWithCurve(ray, settings);
        double left = 0, right = 0;
        for (int i = 0; i < section.SegmentCount; i++)
        {
            using var segment = section.GetCurveSegment(i);
            double a = g.Coord(segment.GetEndPoint(0), axis), b = g.Coord(segment.GetEndPoint(1), axis);
            if (Math.Abs(b - a) > right - left) (left, right) = (Math.Min(a, b), Math.Max(a, b));
        }
        double length = SlabLayoutRules.StirrupLength((right - left + diameter) / Mm) * Mm;
        if (length == 0 || right <= left)
        {
            Empty++;
            return;
        }
        var bottom = station + axis * ((left + right - length) / 2)
            - g.N * (g.Thickness - options.Depths[direction + 2] * Mm + endDiameter / 2);
        double height = g.Thickness - (options.Depths[direction] + options.Depths[direction + 2]) * Mm + endDiameter;
        Stirrups.Add(new(bottom, axis, g.N, length, height, diameter));
    }
}
#endif
