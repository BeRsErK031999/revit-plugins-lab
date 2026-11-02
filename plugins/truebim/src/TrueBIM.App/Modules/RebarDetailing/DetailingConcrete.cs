using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

internal sealed class DetailingSolid
{
    public Solid Solid { get; set; } = null!;
    public Transform Transform { get; set; } = null!;
    public string HostKey { get; set; } = "";
}

internal sealed partial class DetailingGeometry
{
    public List<DetailingSolid> Solids { get; } = new();
    public DetailingRect? PlannedBreakBoundary { get; set; }
    private IReadOnlyList<IReadOnlyList<(double X, double Y)>>? breaks;

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Breaks(DetailingFrame frame)
    {
        if (breaks != null) return breaks;
        var box = PlannedBreakBoundary ?? frame.BreakBoundary;
        var corners = new[] { (X: box.Left, Y: box.Top), (X: box.Left, Y: box.Bottom),
            (X: box.Right, Y: box.Bottom), (X: box.Right, Y: box.Top) };
        var intervals = new List<(int Side, double Start, double End)>();
        using SolidCurveIntersectionOptions options = new() { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
        // Probe just inside the cut plane, avoiding coincident faces. Only real
        // solid intersections count; a bounding box cannot detect an opening.
        for (int side = 0; side < 4; side++)
        {
            var a = corners[side]; var b = corners[(side + 1) % 4];
            XYZ start = frame.Point(a.X, a.Y) + frame.Normal * frame.CutDepth;
            XYZ end = frame.Point(b.X, b.Y) + frame.Normal * frame.CutDepth;
            XYZ along = (end - start).Normalize();
            foreach (var solid in Solids)
            {
                using Line line = Line.CreateBound(solid.Transform.Inverse.OfPoint(start), solid.Transform.Inverse.OfPoint(end));
                using SolidCurveIntersection intersection = solid.Solid.IntersectWithCurve(line, options);
                for (int index = 0; index < intersection.SegmentCount; index++)
                {
                    using Curve segment = intersection.GetCurveSegment(index);
                    double first = (solid.Transform.OfPoint(segment.GetEndPoint(0)) - start).DotProduct(along);
                    double last = (solid.Transform.OfPoint(segment.GetEndPoint(1)) - start).DotProduct(along);
                    intervals.Add((side, first, last));
                }
            }
        }
        breaks = DetailingPerimeter.Trace(box, intervals, 0.1 / 304.8);
        return breaks;
    }
}
