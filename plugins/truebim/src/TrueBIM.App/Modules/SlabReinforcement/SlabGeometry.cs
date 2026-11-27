#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabGeometry
{
    internal const double Mm = 1.0 / 304.8;
    internal const double Tolerance = 0.05 * Mm;
    internal PlanarFace Top { get; }
    internal PlanarFace Bottom { get; }
    internal XYZ X { get; }
    internal XYZ Y { get; }
    internal XYZ N => Top.FaceNormal;
    internal XYZ Origin => Top.Origin;
    internal double Thickness { get; }
    internal IList<CurveLoop> Loops { get; }

    internal SlabGeometry(Floor floor, XYZ direction)
    {
        Top = PlaneFace(floor, HostObjectUtils.GetTopFaces(floor));
        Bottom = PlaneFace(floor, HostObjectUtils.GetBottomFaces(floor));
        if (N.DotProduct(Bottom.FaceNormal) > -0.999999)
            throw new InvalidOperationException("Выберите плиту постоянной толщины с параллельными гранями.");
        X = (direction - N * direction.DotProduct(N)).Normalize();
        if (X.GetLength() < 0.9)
            throw new InvalidOperationException("Выберите прямую кромку в плоскости плиты.");
        Y = N.CrossProduct(X).Normalize();
        Thickness = (Top.Origin - Bottom.Origin).DotProduct(N);
        Loops = Top.GetEdgesAsCurveLoops();
    }

    private static PlanarFace PlaneFace(Floor floor, IList<Reference> references)
    {
        if (references.Count == 1 && floor.GetGeometryObjectFromReference(references[0])is PlanarFace face)
            return face;
        throw new InvalidOperationException("Нужна плита с одной плоской верхней и нижней гранью. Перепады обрабатываются по отдельным плитам.");
    }

    internal double Coord(XYZ point, XYZ axis) => (point - Origin).DotProduct(axis);
    internal XYZ OnTop(XYZ point) => point - N * Coord(point, N);
    internal UV UV(XYZ point) => new(Coord(point, Top.XVector), Coord(point, Top.YVector));
    internal bool Inside(XYZ point) => Top.IsInside(UV(point));
    internal bool SegmentInside(XYZ from, XYZ to)
    {
        var a = OnTop(from);
        var b = OnTop(to);
        if (!Inside(a) || !Inside(b) || !Inside((a + b) / 2))
            return false;
        if (a.DistanceTo(b) < Tolerance)
            return true;
        var test = Line.CreateBound(a, b);
        foreach (var border in EdgesNear(a, b))
            if (border.Intersect(test) == SetComparisonResult.Overlap)
                return false;
        return true;
    }

    internal double EndClearance(XYZ point, double searchRadius)
    {
        XYZ p = OnTop(point);
        XYZ diagonal = (X + Y) * searchRadius;
        return EdgesNear(p - diagonal, p + diagonal).Select(c => c.Distance(p)).DefaultIfEmpty(double.PositiveInfinity).Min();
    }

    internal bool FootprintInside(XYZ[] corners)
    {
        if (!Enumerable.Range(0, 4).All(i => SegmentInside(corners[i], corners[(i + 1) % 4])))
            return false;
        // A small opening may fall completely inside a frame's footprint.
        XYZ a = corners[0], b = corners[2];
        return !EdgesNear(a, b).Any(c =>
        {
            XYZ p = c.GetEndPoint(0);
            return (p - a).DotProduct(X) * (p - b).DotProduct(X) < 0 && (p - a).DotProduct(Y) * (p - b).DotProduct(Y) < 0;
        });
    }

    internal IEnumerable<(double Start, double End)> Intervals(XYZ start, XYZ axis, double length)
    {
        if (length <= Tolerance)
            yield break;
        var end = start + axis * length;
        var ray = Line.CreateBound(start, end);
        var cuts = new SortedSet<double>
        {
            0,
            length
        };
        foreach (var border in EdgesNear(start, end))
            if (border.Intersect(ray, out var hits) == SetComparisonResult.Overlap && hits != null)
                foreach (IntersectionResult hit in hits)
                    cuts.Add((hit.XYZPoint - start).DotProduct(axis));
        double prev = 0;
        foreach (double next in cuts)
        {
            if (next - prev > Tolerance && Inside(start + axis * ((prev + next) / 2)))
                yield return (prev, next);
            prev = next;
        }
    }

    private sealed record EdgeBox(Curve Edge, double X0, double X1, double Y0, double Y1);
    private List<EdgeBox>? boxes;
    private IEnumerable<Curve> EdgesNear(XYZ a, XYZ b)
    {
        boxes ??= Loops.SelectMany(l => l).Select(c =>
        {
            var horizontal = Range(X, new[] { c });
            var vertical = Range(Y, new[] { c });
            return new EdgeBox(c, horizontal.Min - Tolerance, horizontal.Max + Tolerance,
                vertical.Min - Tolerance, vertical.Max + Tolerance);
        }).ToList();
        double x0 = Coord(a, X), x1 = Coord(b, X), y0 = Coord(a, Y), y1 = Coord(b, Y);
        return boxes.Where(e => e.X0 <= Math.Max(x0, x1) && e.X1 >= Math.Min(x0, x1) && e.Y0 <= Math.Max(y0, y1) && e.Y1 >= Math.Min(y0, y1)).Select(e => e.Edge);
    }

    internal (double Min, double Max) Range(XYZ axis, IEnumerable<Curve>? outline = null)
    {
        var ends = new List<double>();
        foreach (Curve c in outline ?? Loops.SelectMany(loop => loop))
        {
            ends.Add(Coord(c.GetEndPoint(0), axis));
            ends.Add(Coord(c.GetEndPoint(1), axis));
            if (c is Arc arc)
            {
                double angle = Math.Atan2(axis.DotProduct(arc.YDirection), axis.DotProduct(arc.XDirection));
                for (int i = -2; i <= 4; i++)
                {
                    double t = angle + i * Math.PI;
                    if (t > arc.GetEndParameter(0) && t < arc.GetEndParameter(1))
                        ends.Add(Coord(arc.Evaluate(t, false), axis));
                }
            }
            else if (!(c is Line))
                throw new InvalidOperationException("Поддерживаются прямые и дуговые кромки плиты.");
        }

        return (ends.Min(), ends.Max());
    }
}
#endif
