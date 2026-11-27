using Autodesk.Revit.DB;
using System.Windows;
using System.Windows.Media;
using Transform = Autodesk.Revit.DB.Transform;
using Point = System.Windows.Point;
using Shape = System.Windows.Media.Geometry;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Horizontal sections of actual slab faces in the project and loaded Revit links.</summary>
internal sealed class FinishVerticalObstacles
{
    private const double Tolerance = 1e-5;
    private sealed record Surface(XYZ A, XYZ B, XYZ C, Rect Bounds);
    private sealed record Slab(Floor Floor, Transform Transform, Rect Bounds, double Low, double High)
    {
        internal List<Surface>? Surfaces { get; set; }
    }
    private readonly List<Slab> slabs = [];

    internal FinishVerticalObstacles(Document document)
    {
        Collect(document, Transform.Identity);
        foreach (var link in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>())
            if (link.GetLinkDocument() is Document linked) Collect(linked, link.GetTotalTransform());
    }

    private void Collect(Document document, Transform transform)
    {
        foreach (Floor floor in new FilteredElementCollector(document).OfClass(typeof(Floor)))
        {
            if (floor.get_BoundingBox(null) is not { } box) continue;
            var corners = (from x in new[] { box.Min.X, box.Max.X }
                           from y in new[] { box.Min.Y, box.Max.Y }
                           from z in new[] { box.Min.Z, box.Max.Z }
                           select transform.OfPoint(box.Transform.OfPoint(new XYZ(x, y, z)))).ToArray();
            slabs.Add(new(floor, transform, Bounds(corners), corners.Min(p => p.Z), corners.Max(p => p.Z)));
        }
    }

    internal Shape Visible(Shape footprint, double from, double to, Element? ignore = null)
    {
        var low = Math.Min(from, to) + Tolerance;
        var high = Math.Max(from, to) - Tolerance;
        if (high <= low) return footprint;
        foreach (var slab in slabs)
        {
            if (slab.High < low || slab.Low > high || !slab.Bounds.IntersectsWith(footprint.Bounds)
                || (ignore != null && slab.Floor.Document.Equals(ignore.Document) && slab.Floor.Id == ignore.Id && slab.Transform.IsIdentity)) continue;
            slab.Surfaces ??= Read(slab);
            foreach (var face in slab.Surfaces)
            {
                if (!face.Bounds.IntersectsWith(footprint.Bounds)) continue;
                var polygon = Clip(Clip(new List<XYZ> { face.A, face.B, face.C }, low, true), high, false);
                if (polygon.Count < 3) continue;
                footprint = Shape.Combine(footprint, Outline(polygon), GeometryCombineMode.Exclude, null, Tolerance, ToleranceType.Absolute);
                if (footprint.Bounds.IsEmpty) return footprint;
            }
        }
        return footprint;
    }

    private static List<Surface> Read(Slab slab)
    {
        List<Surface> surfaces = [];
        foreach (var reference in HostObjectUtils.GetTopFaces(slab.Floor).Concat(HostObjectUtils.GetBottomFaces(slab.Floor)))
        {
            if (slab.Floor.GetGeometryObjectFromReference(reference) is not Face face || face.Triangulate() is not Mesh mesh) continue;
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                var triangle = mesh.get_Triangle(i);
                var points = Enumerable.Range(0, 3).Select(k => slab.Transform.OfPoint(triangle.get_Vertex(k))).ToArray();
                surfaces.Add(new(points[0], points[1], points[2], Bounds(points)));
            }
        }
        return surfaces;
    }

    private static List<XYZ> Clip(List<XYZ> points, double elevation, bool above)
    {
        List<XYZ> result = [];
        if (points.Count == 0) return result;
        var previous = points[points.Count - 1];
        bool Inside(XYZ p) => above ? p.Z >= elevation : p.Z <= elevation;
        foreach (var current in points)
        {
            if (Inside(current) != Inside(previous))
                result.Add(previous + (current - previous) * ((elevation - previous.Z) / (current.Z - previous.Z)));
            if (Inside(current)) result.Add(current);
            previous = current;
        }
        return result;
    }

    internal static Shape Outline(IEnumerable<XYZ> vertices)
    {
        var points = vertices.Select(p => new Point(p.X, p.Y)).ToList();
        var shape = new StreamGeometry();
        using (var context = shape.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }
        shape.Freeze();
        return shape;
    }

    private static Rect Bounds(IEnumerable<XYZ> points)
    {
        var result = Rect.Empty;
        foreach (var p in points) result.Union(new Point(p.X, p.Y));
        return result;
    }
}
