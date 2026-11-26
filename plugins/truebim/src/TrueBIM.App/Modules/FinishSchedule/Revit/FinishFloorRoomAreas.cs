using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;
using System.Windows;
using System.Windows.Media;
using Shape = System.Windows.Media.Geometry;
using Point = System.Windows.Point;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Measures the horizontal projection of floor finish inside disjoint room footprints.</summary>
internal sealed class FinishFloorRoomAreas
{
    // All planar operations use Revit internal feet.
    private const double PolygonTolerance = 0.00001;
    private const double MinimumArea = 1e-8;
    private sealed record Footprint(long Id, long Level, Shape Polygon, double BaseZ, double Height, bool DoubleHeightVoid, ElementId Phase);
    private sealed record Triangle(Shape Outline, double TopZ);

    internal static FinishQuantityResult Calculate(Document doc, FinishQuantityRequest request, FinishVerticalObstacles obstacles)
    {
        var rooms = new List<Footprint>();
        var invalid = new List<FinishRoomCandidateSnapshot>();
        foreach (var candidate in request.Rooms)
        {
            var room = doc.GetElement(RevitElementIds.Create(candidate.ElementId)) as Room;
            if (room == null || candidate.Bounds == null) continue;
            try
            {
                using var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
                var loops = room.GetBoundarySegments(options);
                if (loops == null || loops.Count == 0) { invalid.Add(candidate); continue; }
                var outline = new StreamGeometry { FillRule = FillRule.EvenOdd };
                using (var writer = outline.Open())
                    foreach (var loop in loops)
                    {
                        var points = new List<Point>();
                        foreach (var segment in loop)
                        {
                            var curve = segment.GetCurve().Tessellate();
                            points.AddRange(curve.Take(curve.Count - 1).Select(p => new Point(p.X, p.Y)));
                        }
                        if (points.Count < 3) throw new InvalidOperationException("Пустая петля помещения.");
                        writer.BeginFigure(points[0], true, true);
                        writer.PolyLineTo(points.Skip(1).ToArray(), true, false);
                    }
                outline.Freeze();
                rooms.Add(new(candidate.ElementId, RevitElementIds.GetValue(room.LevelId), outline, candidate.Bounds.MinZ, candidate.Bounds.MaxZ - candidate.Bounds.MinZ,
                    candidate.Name.IndexOf("второй свет", StringComparison.OrdinalIgnoreCase) >= 0, room.get_Parameter(BuiltInParameter.ROOM_PHASE).AsElementId()));
            }
            catch (Exception) { invalid.Add(candidate); }
        }
        var hits = new List<FinishOccurrence>();
        var warnings = new List<FinishGeometryWarning>();
        foreach (var item in request.Elements.Where(e => e.Category == FinishPreviewCategory.Floors))
        {
            var id = item.Element.ElementId;
            var floor = doc.GetElement(RevitElementIds.Create(id)) as Floor;
            if (floor == null) continue;
            try
            {
                var triangles = ReadTop(floor);
                var extent = Rect.Empty;
                foreach (var triangle in triangles) extent.Union(triangle.Outline.Bounds);
                var level = RevitElementIds.GetValue(floor.LevelId);
                if (invalid.Any(r => r.LevelId == level && r.Bounds != null && extent.IntersectsWith(
                    new Rect(new Point(r.Bounds.MinX, r.Bounds.MinY), new Point(r.Bounds.MaxX, r.Bounds.MaxY)))))
                    throw new InvalidOperationException("В области пола не удалось прочитать контур помещения.");
                var floorTop = triangles.Count == 0 ? double.MinValue : triangles.Max(t => t.TopZ);
                var candidates = rooms.Where(r => extent.IntersectsWith(r.Polygon.Bounds)
                    && (r.Level == level || CanReachBelow(r, floorTop))
                    && floor.GetPhaseStatus(r.Phase) is ElementOnPhaseStatus.New or ElementOnPhaseStatus.Existing).ToArray();
                var patches = new Dictionary<long, Shape>();
                foreach (var triangle in triangles)
                {
                    var pieces = new List<(long Id, ElementId Phase, bool SameLevel, double Distance, Shape Shape)>();
                    foreach (var room in candidates)
                    {
                        if (!room.Polygon.Bounds.IntersectsWith(triangle.Outline.Bounds)) continue;
                        var sameLevel = room.Level == level;
                        if (!sameLevel && !CanReachBelow(room, triangle.TopZ)) continue;
                        var piece = Combine(triangle.Outline, room.Polygon, GeometryCombineMode.Intersect);
                        if (triangle.TopZ < room.BaseZ - PolygonTolerance)
                            piece = obstacles.Visible(piece, triangle.TopZ, room.BaseZ, floor);
                        // A floor may be modelled on the upper datum with a negative offset.
                        // Remove the plan region occupied by an intervening, lower room.
                        foreach (var lower in rooms.Where(r => triangle.TopZ < room.BaseZ - PolygonTolerance && r.Level != room.Level && r.Phase == room.Phase && r.BaseZ < room.BaseZ - PolygonTolerance
                            && r.BaseZ + r.Height > triangle.TopZ + PolygonTolerance && r.Polygon.Bounds.IntersectsWith(piece.Bounds)))
                            piece = Combine(piece, lower.Polygon, GeometryCombineMode.Exclude);
                        if (Area(piece) > MinimumArea) pieces.Add((room.Id, room.Phase, sameLevel, Math.Abs(triangle.TopZ - room.BaseZ), piece));
                    }
                    foreach (var piece in pieces)
                    {
                        Shape unique = piece.Shape;
                        foreach (var other in pieces.Where(p => p.Id != piece.Id && p.Phase == piece.Phase && (p.SameLevel && !piece.SameLevel || p.SameLevel == piece.SameLevel && p.Distance <= piece.Distance + PolygonTolerance) && p.Shape.Bounds.IntersectsWith(piece.Shape.Bounds)))
                            unique = Combine(unique, other.Shape, GeometryCombineMode.Exclude);
                        if (Area(unique) <= MinimumArea) continue;
                        patches[piece.Id] = patches.TryGetValue(piece.Id, out var prior)
                            ? Combine(prior, unique, GeometryCombineMode.Union) : unique;
                    }
                }
                if (triangles.Count == 0) throw new InvalidOperationException("Недоступна проекция верхней поверхности.");
                if (patches.Count == 0)
                    warnings.Add(new(FinishGeometryWarningCode.OutsideRoomProjection, $"Пол {id} вне проекции подходящих помещений. В ведомости учтено 0 м².", ElementId: id, Category: FinishPreviewCategory.Floors));
                foreach (var patch in patches)
                    hits.Add(new FinishOccurrence(patch.Key, id, FinishPreviewCategory.Floors, RevitAreaUnits.ToSquareMeters(Area(patch.Value)), FinishQuantityMethod.FloorRoomSurface));
            }
            catch (Exception ex)
            {
                warnings.Add(new(FinishGeometryWarningCode.ProjectedAreaUnavailable, $"Пол {id}: площадь по помещениям не определена. {ex.Message}",
                    ElementId: id, Category: FinishPreviewCategory.Floors));
            }
        }
        var roomPhases = rooms.ToDictionary(room => room.Id, room => RevitElementIds.GetValue(room.Phase));
        var resolved = new FinishElementOwnershipResolver().ResolveFloorShares(hits, roomPhases);
        return new FinishQuantityResult(resolved.Occurrences, warnings.Concat(resolved.Warnings));
    }

    // Cross-datum recovery is local to the room. The midpoint of its vertical span
    // separates a nearby mislevelled finish from a blind search through the storey below.
    private static bool CanReachBelow(Footprint room, double top) =>
        !room.DoubleHeightVoid && top <= room.BaseZ + PolygonTolerance && room.BaseZ - top < room.Height / 2;

    private static List<Triangle> ReadTop(Floor floor)
    {
        var result = new List<Triangle>();
        foreach (var reference in HostObjectUtils.GetTopFaces(floor))
        {
            var face = floor.GetGeometryObjectFromReference(reference) as Face
                ?? throw new InvalidOperationException("Отсутствует верхняя грань.");
            var mesh = face.Triangulate();
            if (mesh == null)
            {
                // Revit can omit a small planar facet from the mesh. Use its loops with their holes.
                if (face is not PlanarFace plane || Math.Abs(plane.FaceNormal.Z) < 1e-8)
                    throw new InvalidOperationException("Revit не вернул сетку верхней грани.");
                var outline = new StreamGeometry { FillRule = FillRule.EvenOdd };
                using (var writer = outline.Open())
                    foreach (var loop in face.GetEdgesAsCurveLoops())
                    {
                        var pts = new List<Point>();
                        XYZ? end = null;
                        foreach (var curve in loop)
                        {
                            var v = curve.Tessellate();
                            end = curve.GetEndPoint(1);
                            if (curve is not Line && v.Count < 8)
                                pts.AddRange(Enumerable.Range(0, 8).Select(i => curve.Evaluate(i / 8.0, true)).Select(p => new Point(p.X, p.Y)));
                            else
                                pts.AddRange(v.Take(v.Count - 1).Select(p => new Point(p.X, p.Y)));
                        }
                        // Revit may close a two-edge sliver within its modelling tolerance.
                        if (end != null) pts.Add(new Point(end.X, end.Y));
                        if (pts.Count < 3)
                        {
                            if (face.Area <= MinimumArea) continue;
                            throw new InvalidOperationException("Не удалось восстановить контур верхней грани.");
                        }
                        writer.BeginFigure(pts[0], true, true);
                        writer.PolyLineTo(pts.Skip(1).ToArray(), true, false);
                    }
                result.Add(new(outline, plane.Origin.Z));
                continue;
            }
            for (int i = 0; i < mesh.NumTriangles; i++)
            {
                var t = mesh.get_Triangle(i);
                var a = t.get_Vertex(0); var b = t.get_Vertex(1); var c = t.get_Vertex(2);
                var normal = (b - a).CrossProduct(c - a);
                if (Math.Abs(normal.Z) <= MinimumArea) continue;
                var polygon = new StreamGeometry();
                using (var writer = polygon.Open())
                {
                    writer.BeginFigure(new Point(a.X, a.Y), true, true);
                    writer.LineTo(new Point(b.X, b.Y), true, false);
                    writer.LineTo(new Point(c.X, c.Y), true, false);
                }
                result.Add(new(polygon, Math.Max(a.Z, Math.Max(b.Z, c.Z))));
            }
        }
        return result;
    }
    private static double Area(Shape geometry) => geometry.GetArea(PolygonTolerance, ToleranceType.Absolute);
    private static Shape Combine(Shape a, Shape b, GeometryCombineMode mode) =>
        Shape.Combine(a, b, mode, null, PolygonTolerance, ToleranceType.Absolute);
}
