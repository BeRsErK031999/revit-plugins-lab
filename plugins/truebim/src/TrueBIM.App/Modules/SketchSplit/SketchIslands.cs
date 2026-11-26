#if REVIT2022_OR_GREATER
using Autodesk.Revit.DB;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace TrueBIM.App.Modules.SketchSplit;

internal sealed record SketchIsland(GeometryGroup Shape, IReadOnlyList<ElementId> Curves)
{
    public double Area => Shape.GetArea(0.00001, ToleranceType.Absolute);
}

internal sealed class SketchIslands
{
    private sealed record Loop(StreamGeometry Shape, List<ElementId> Curves);
    public static Sketch? GetSketch(Element element) => element.Document.GetElement(element switch
    {
        Floor floor => floor.SketchId,
        Ceiling ceiling => ceiling.SketchId,
        _ => ElementId.InvalidElementId
    }) as Sketch;

    public static List<SketchIsland> Read(Sketch sketch)
    {
        var loops = new List<Loop>();
        var sketchLines = sketch.GetAllElements().Select(sketch.Document.GetElement).OfType<ModelCurve>().ToArray();
        foreach (CurveArray boundary in sketch.Profile)
        {
            var shape = new StreamGeometry();
            var vertices = new List<Point>();
            var ids = new List<ElementId>();
            foreach (Curve curve in boundary)
            {
                var model = sketchLines.FirstOrDefault(x => x.Id == curve.Reference?.ElementId)
                    ?? sketchLines.SingleOrDefault(x => SameCurve(x.GeometryCurve, curve))
                    ?? throw new InvalidOperationException("Не удалось найти линию эскиза для границы.");
                ids.Add(model.Id);
                var points = curve.Tessellate();
                vertices.AddRange(points.Take(points.Count - 1).Select(p => new Point(p.X, p.Y)));
            }
            if (vertices.Count < 3) throw new InvalidOperationException("Петля эскиза вырождена.");
            using (var context = shape.Open())
            {
                context.BeginFigure(vertices[0], true, true);
                context.PolyLineTo(vertices.Skip(1).ToArray(), true, false);
            }
            shape.Freeze();
            loops.Add(new(shape, ids));
        }
        var parents = new Dictionary<Loop, Loop?>();
        foreach (var loop in loops)
        {
            var containers = new List<Loop>();
            foreach (var other in loops.Where(p => p != loop))
            {
                var relation = other.Shape.FillContainsWithDetail(loop.Shape, 0.00001, ToleranceType.Absolute);
                if (relation == IntersectionDetail.FullyContains) containers.Add(other);
                if (relation == IntersectionDetail.Intersects) throw new InvalidOperationException("Контуры эскиза пересекаются или касаются.");
            }
            parents[loop] = containers.OrderBy(x => x.Shape.GetArea()).FirstOrDefault();
        }
        bool IsIsland(Loop loop)
        {
            int depth = 0;
            for (var parent = parents[loop]; parent != null; parent = parents[parent])
                if (++depth > loops.Count) throw new InvalidOperationException("Неоднозначное вложение петель.");
            return depth % 2 == 0;
        }
        var islands = new List<SketchIsland>();
        foreach (var outer in loops.Where(IsIsland))
        {
            var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            var ids = new List<ElementId>();
            foreach (var loop in loops.Where(x => x == outer || parents[x] == outer))
            {
                group.Children.Add(loop.Shape);
                ids.AddRange(loop.Curves);
            }
            group.Freeze();
            islands.Add(new(group, ids));
        }
        return islands.OrderByDescending(i => i.Area).ThenBy(i => i.Shape.Bounds.Left).ThenBy(i => i.Shape.Bounds.Top).ToList();
    }

    internal static bool SameCurve(Curve a, Curve b)
    {
        if (a.GetType() != b.GetType() || Math.Abs(a.Length - b.Length) > 1e-6) return false;
        var start = a.GetEndPoint(0);
        return (start.IsAlmostEqualTo(b.GetEndPoint(0)) || start.IsAlmostEqualTo(b.GetEndPoint(1)))
            && a.Evaluate(.5, true).IsAlmostEqualTo(b.Evaluate(.5, true));
    }
}
#endif
