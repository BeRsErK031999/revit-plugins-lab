using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace TrueBIM.App.Modules.GeometricFinishes;

public static class FinishGeometry
{
    public const double FeetPerMm = 1.0 / 304.8;
    public static double RoomBase(Room room) => ((Level)room.Document.GetElement(room.LevelId)).Elevation
        + (room.get_Parameter(BuiltInParameter.ROOM_LOWER_OFFSET)?.AsDouble() ?? 0);
    public static double RoomTop(Room room) => RoomBase(room) + room.UnboundedHeight;

    public static CurveLoop ClosedRoomLoop(IList<BoundarySegment> segments, Action<string> warning)
    {
        var curves = segments.Select(s => s.GetCurve()).ToList();
        double maximumGap = 0;
        for (int i = 0; i < curves.Count; i++)
        {
            int next = (i + 1) % curves.Count;
            XYZ a = curves[i].GetEndPoint(1), b = curves[next].GetEndPoint(0);
            double gap = a.DistanceTo(b);
            if (gap < 1e-8) continue;
            if (gap > FeetPerMm) throw new InvalidOperationException($"Разрыв контура {gap * 304.8:F2} мм превышает допуск исправления 1 мм.");
            if (curves[i] is Line) curves[i] = Line.CreateBound(curves[i].GetEndPoint(0), b);
            else if (curves[next] is Line) curves[next] = Line.CreateBound(a, curves[next].GetEndPoint(1));
            else throw new InvalidOperationException("Разрыв между дугами контура. Исправьте границу помещения.");
            maximumGap = Math.Max(maximumGap, gap);
        }
        if (maximumGap > 0.01 * FeetPerMm) warning($"Совмещены концы прямых на стыке контура: максимальный зазор {maximumGap * 304.8:F2} мм (допуск 1 мм).");
        return CurveLoop.Create(curves);
    }

    public static IList<CurveLoop> AtElevation(IList<CurveLoop> loops, double z)
    {
        return loops.Select(loop => CurveLoop.Create(loop.Select(curve =>
        {
            if (curve is not Line && curve is not Arc) throw new InvalidOperationException("Контур содержит кривую, отличную от прямой или дуги.");
            if (Math.Abs(curve.GetEndPoint(0).Z - curve.GetEndPoint(1).Z) > 1e-7)
                throw new InvalidOperationException("Контур помещения не горизонтален.");
            return curve.CreateTransformed(Transform.CreateTranslation(XYZ.BasisZ * (z - curve.GetEndPoint(0).Z)));
        }).ToList())).ToList();
    }

    public static Curve WallCenterline(FinishTarget target, WallType type)
    {
        Curve boundary = target.Boundary!;
        if (boundary is not Line && boundary is not Arc) throw new InvalidOperationException("Для стены поддерживаются прямые и дуговые границы.");
        XYZ middle = boundary.Evaluate(0.5, true);
        XYZ tangent = boundary.ComputeDerivatives(0.5, true).BasisX.Normalize();
        XYZ left = XYZ.BasisZ.CrossProduct(tangent).Normalize();
        double sampleZ = RoomBase(target.Room) + Math.Min(target.Room.UnboundedHeight / 2, 300 * FeetPerMm);
        XYZ sample = new(middle.X, middle.Y, sampleZ);
        bool? leftIsInside = null;
        foreach (double distance in new[] { 5.0, 1.0, 20.0, 50.0 })
        {
            bool a = target.Room.IsPointInRoom(sample + left * (distance * FeetPerMm));
            bool b = target.Room.IsPointInRoom(sample - left * (distance * FeetPerMm));
            if (a != b) { leftIsInside = a; break; }
        }
        if (leftIsInside == null) throw new InvalidOperationException("Не удалось определить внутреннюю сторону границы. Проверьте контур помещения.");
        XYZ inside = leftIsInside.Value ? left : -left;
        Curve plus = boundary.CreateOffset(type.Width / 2, XYZ.BasisZ);
        Curve minus = boundary.CreateOffset(-type.Width / 2, XYZ.BasisZ);
        return (plus.Evaluate(0.5, true) - middle).DotProduct(inside) > (minus.Evaluate(0.5, true) - middle).DotProduct(inside) ? plus : minus;
    }

    public static IReadOnlyList<IList<CurveLoop>> Regions(IList<CurveLoop> loops)
    {
        // Revit < 2022 needs a separate floor for every exterior and explicit inner openings.
        var ordered = loops.Select(loop => new { Loop = loop, Points = loop.SelectMany(c => c.Tessellate().Take(c.Tessellate().Count - 1)).ToArray() })
            .OrderByDescending(x => Math.Abs(SignedArea(x.Points))).ToList();
        var groups = new List<IList<CurveLoop>>();
        foreach (var candidate in ordered)
        {
            var parent = groups.FirstOrDefault(g => Contains(g[0].SelectMany(c => c.Tessellate()).ToArray(), candidate.Points[0]));
            if (parent == null) groups.Add(new List<CurveLoop> { candidate.Loop });
            else parent.Add(candidate.Loop);
        }
        return groups;
    }
    private static double SignedArea(IList<XYZ> points)
    {
        double area = 0;
        for (int i = 0; i < points.Count; i++) { XYZ a = points[i]; XYZ b = points[(i + 1) % points.Count]; area += a.X * b.Y - b.X * a.Y; }
        return area / 2;
    }
    private static bool Contains(IList<XYZ> polygon, XYZ point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            XYZ a = polygon[i], b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
}

public sealed class FinishSlabLocator
{
    private readonly List<(Face Face, Transform Transform, bool Top)> faces = new();
    public FinishSlabLocator(Document doc)
    {
        Collect(doc, Transform.Identity);
        foreach (RevitLinkInstance link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)))
        {
            Document? linked = link.GetLinkDocument();
            if (linked != null) Collect(linked, link.GetTotalTransform());
        }
    }
    private void Collect(Document doc, Transform transform)
    {
        foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)))
        {
            if (FinishOwnership.IsManaged(floor)) continue;
            foreach (bool top in new[] { true, false })
            {
                foreach (Reference reference in top ? HostObjectUtils.GetTopFaces(floor) : HostObjectUtils.GetBottomFaces(floor))
                    if (floor.GetGeometryObjectFromReference(reference) is Face face) faces.Add((face, transform, top));
            }
        }
    }
    public double Find(Room room, bool ceiling)
    {
        if (room.Location is not LocationPoint location) throw new InvalidOperationException("У помещения нет точки размещения.");
        XYZ origin = location.Point;
        double reference = ceiling ? FinishGeometry.RoomTop(room) : FinishGeometry.RoomBase(room);
        var candidates = new List<double>();
        bool slope = false;
        foreach (var item in faces.Where(f => f.Top != ceiling))
        {
            if (item.Face is not PlanarFace plane) continue;
            XYZ normal = item.Transform.OfVector(plane.FaceNormal);
            XYZ local = item.Transform.Inverse.OfPoint(new XYZ(origin.X, origin.Y, reference));
            IntersectionResult projection = plane.Project(local);
            if (projection == null || !plane.IsInside(projection.UVPoint)) continue;
            double z = item.Transform.OfPoint(projection.XYZPoint).Z;
            // The underside of an upper slab is commonly below the room's upper limit.
            double distance = ceiling ? Math.Abs(z - reference) : reference - z;
            if (distance < -20 * FinishGeometry.FeetPerMm || distance > 3000 * FinishGeometry.FeetPerMm
                || ceiling && z <= FinishGeometry.RoomBase(room) + room.UnboundedHeight / 2) continue;
            if (Math.Abs(normal.Normalize().Z) < 0.999999) { slope = true; continue; }
            candidates.Add(z);
        }
        if (candidates.Count == 0) throw new InvalidOperationException(slope
            ? "Найдено наклонное перекрытие. Выберите фиксированную отметку; автоматическое прилегание к уклонам пока не поддерживается."
            : "Не найдено горизонтальное перекрытие над/под помещением в пределах 3 м от его границы. Выберите отметку от уровня.");
        return ceiling ? candidates.OrderBy(z => Math.Abs(z - reference)).First() : candidates.Max();
    }
}
