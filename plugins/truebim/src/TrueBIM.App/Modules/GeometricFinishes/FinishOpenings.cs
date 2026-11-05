using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.GeometricFinishes;

/// <summary>Transfers the actual wall cutout, not a door/window bounding box (which includes trim and swing).</summary>
public static class FinishOpenings
{
    private sealed class CutoutFace
    {
        public PlanarFace Face = null!;
        public IReadOnlyList<Curve> Curves = Array.Empty<Curve>();
        public IReadOnlyList<PlanarFace> Surfaces = Array.Empty<PlanarFace>();
        public double Distance;
    }
    public static IReadOnlyList<Curve> InstanceCutout(Wall source, FamilyInstance insert, XYZ? boundaryPoint = null, XYZ? boundaryDirection = null)
        => FindCutoutFace(source, insert, boundaryPoint, boundaryDirection)?.Curves
            ?? throw new InvalidOperationException("Не найден фактический вырез семейства " + insert.Id + " на грани стены " + source.Id + ".");

    private static CutoutFace? FindCutoutFace(Wall source, FamilyInstance insert, XYZ? boundaryPoint, XYZ? boundaryDirection)
    {
        Curve axis = ((LocationCurve)source.Location).Curve;
        // A room boundary can follow a wall end face, especially where a door reaches a joined corner.
        XYZ normal = (boundaryDirection ?? axis.ComputeDerivatives(0.5, true).BasisX).Normalize().CrossProduct(XYZ.BasisZ);
        var candidates = new List<CutoutFace>();
        var faces = source.get_Geometry(new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine })
            .OfType<Solid>().SelectMany(s => s.Faces.OfType<PlanarFace>()).ToArray();
        // Generating-element IDs identify the edges made by this insert on the actual host side face.
        foreach (PlanarFace face in faces)
        {
            if (Math.Abs(face.FaceNormal.DotProduct(normal)) < 0.9999) continue;
            var curves = new List<Curve>();
            foreach (EdgeArray loop in face.EdgeLoops)
            foreach (Edge edge in loop)
                if (source.GetGeneratingElementIds(edge).Contains(insert.Id)) curves.Add(edge.AsCurve());
            if (curves.Count < 2) continue;
            double distance = boundaryPoint == null ? 0 : Math.Abs((boundaryPoint - face.Origin).DotProduct(face.FaceNormal));
            candidates.Add(new CutoutFace { Face = face, Curves = curves, Distance = distance,
                Surfaces = faces.Where(f => f.FaceNormal.DotProduct(face.FaceNormal) > 0.9999
                    && Math.Abs((f.Origin - face.Origin).DotProduct(face.FaceNormal)) < 1e-6).ToArray() });
        }
        return candidates.OrderBy(c => c.Distance).FirstOrDefault();
    }

    public static IReadOnlyList<FinishOpeningRectangle> Capture(FinishTarget target, double baseZ, double topZ)
    {
        FinishSourceWall source = target.Source!;
        Curve center = target.Boundary!;
        XYZ start = center.GetEndPoint(0), end = center.GetEndPoint(1);
        XYZ direction = (end - start).Normalize();
        double length = center.Length;
        var cutRectangles = new List<(double Left, double Right, double Bottom, double Top)>();
        foreach (ElementId id in source.Wall.FindInserts(true, false, true, true))
        {
            Element? insert = source.Wall.Document.GetElement(id);
            if (insert == null) continue;
            // FindInserts may contain shared nested inserts. The outer host cut defines the opening.
            if (insert is FamilyInstance nested && nested.SuperComponent != null) continue;
            BoundingBoxXYZ? box = insert.get_BoundingBox(null);
            if (box == null) continue;
            XYZ[] corners = Corners(box).Select(source.Transform.OfPoint).ToArray();
            double roughLeft = corners.Min(p => (p - start).DotProduct(direction));
            double roughRight = corners.Max(p => (p - start).DotProduct(direction));
            if (roughRight <= 0 || roughLeft >= length || corners.Max(p => p.Z) <= baseZ || corners.Min(p => p.Z) >= topZ) continue;
            if (center is not Line) throw new InvalidOperationException("Проём в дуговой стене требует отдельной обработки; помещение пропущено без частичной отделки.");
            IReadOnlyList<Curve> curves;
            if (insert is FamilyInstance family)
            {
                CutoutFace? cutout = FindCutoutFace(source.Wall, family, source.Transform.Inverse.OfPoint(target.Boundary!.Evaluate(0.5, true)),
                    source.Transform.Inverse.OfVector(target.Boundary.ComputeDerivatives(0.5, true).BasisX));
                if (cutout == null)
                {
                    // Joined walls can list a neighbour's insert even when it makes no cut on this face.
                    if (family.Host?.Id != source.Wall.Id) continue;
                    throw new InvalidOperationException("Не найден фактический вырез семейства " + insert.Id + " на грани стены " + source.Wall.Id + ".");
                }
                cutRectangles.AddRange(RectanglesOnFace(cutout, source.Transform, start, direction, length, baseZ, topZ, id));
                continue;
            }
            else if (insert is Opening opening)
            {
                if (opening.IsRectBoundary)
                {
                    IList<XYZ> rect = opening.BoundaryRect;
                    XYZ a = source.Transform.OfPoint(rect[0]), b = source.Transform.OfPoint(rect[1]);
                    double s0 = (a - start).DotProduct(direction), s1 = (b - start).DotProduct(direction);
                    cutRectangles.Add((Math.Min(s0, s1), Math.Max(s0, s1), Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z)));
                    continue;
                }
                curves = opening.BoundaryCurves.Cast<Curve>().ToArray();
            }
            else throw new InvalidOperationException("В стене есть встроенный элемент с неподдерживаемым контуром проёма: " + id + ".");
            Curve[] transformed = curves.Select(c => c.CreateTransformed(source.Transform)).ToArray();
            if (transformed.Any(c => c is not Line)) throw new InvalidOperationException("Проём " + id + " имеет криволинейный контур. Прямоугольная подмена не выполняется.");
            XYZ[] points = transformed.SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).ToArray();
            double left = points.Min(p => (p - start).DotProduct(direction));
            double right = points.Max(p => (p - start).DotProduct(direction));
            double bottom = points.Min(p => p.Z), top = points.Max(p => p.Z);
            const double tolerance = 0.5 / 304.8;
            bool rectangle = transformed.All(c =>
            {
                XYZ a = c.GetEndPoint(0), b = c.GetEndPoint(1);
                double sa = (a - start).DotProduct(direction), sb = (b - start).DotProduct(direction);
                return (Math.Abs(sa - sb) < tolerance && (Math.Abs(sa - left) < tolerance || Math.Abs(sa - right) < tolerance))
                    || (Math.Abs(a.Z - b.Z) < tolerance && (Math.Abs(a.Z - bottom) < tolerance || Math.Abs(a.Z - top) < tolerance));
            });
            if (!rectangle) throw new InvalidOperationException("Проём " + id + " не прямоугольный. Помещение пропущено, чтобы не исказить его форму.");
            cutRectangles.Add((left, right, bottom, top));
        }
        return cutRectangles.Distinct().Select(c => new FinishOpeningRectangle { Left = c.Left, Right = c.Right, Bottom = c.Bottom, Top = c.Top }).ToArray();
    }

    private static IEnumerable<(double Left, double Right, double Bottom, double Top)> RectanglesOnFace(
        CutoutFace cutout, Transform transform, XYZ start, XYZ direction, double length, double baseZ, double topZ, ElementId insertId)
    {
        Curve[] curves = cutout.Curves.Select(c => c.CreateTransformed(transform)).ToArray();
        const double tolerance = 0.01 / 304.8;
        if (curves.Any(c => c is not Line || Math.Abs((c.GetEndPoint(1) - c.GetEndPoint(0)).DotProduct(direction)) > tolerance
            && Math.Abs(c.GetEndPoint(1).Z - c.GetEndPoint(0).Z) > tolerance))
            throw new InvalidOperationException("Проём " + insertId + " имеет наклонную или криволинейную границу. Его форма не заменяется прямоугольником.");
        XYZ[] vertices = curves.SelectMany(c => new[] { c.GetEndPoint(0), c.GetEndPoint(1) }).ToArray();
        double[] xs = Unique(vertices.Select(p => Math.Max(0, Math.Min(length, (p - start).DotProduct(direction)))));
        double[] zs = Unique(vertices.Select(p => Math.Max(baseZ, Math.Min(topZ, p.Z))));
        var result = new List<(double Left, double Right, double Bottom, double Top)>();
        for (int z = 0; z < zs.Length - 1; z++)
        {
            double? runStart = null;
            for (int x = 0; x < xs.Length - 1; x++)
            {
                XYZ p = start + direction * ((xs[x] + xs[x + 1]) / 2);
                XYZ local = transform.Inverse.OfPoint(new XYZ(p.X, p.Y, (zs[z] + zs[z + 1]) / 2));
                bool solid = cutout.Surfaces.Any(face => face.IsInside(new UV((local - face.Origin).DotProduct(face.XVector), (local - face.Origin).DotProduct(face.YVector))));
                if (!solid && runStart == null) runStart = xs[x];
                if (runStart != null && (solid || x == xs.Length - 2))
                {
                    double right = solid ? xs[x] : xs[x + 1];
                    int previous = result.FindIndex(r => Math.Abs(r.Left - runStart.Value) < tolerance && Math.Abs(r.Right - right) < tolerance && Math.Abs(r.Top - zs[z]) < tolerance);
                    if (previous >= 0) result[previous] = (result[previous].Left, right, result[previous].Bottom, zs[z + 1]);
                    else result.Add((runStart.Value, right, zs[z], zs[z + 1]));
                    runStart = null;
                }
            }
        }
        return result;
    }
    private static double[] Unique(IEnumerable<double> numbers)
    {
        var values = new List<double>();
        foreach (double number in numbers.OrderBy(n => n))
            if (values.Count == 0 || number - values[values.Count - 1] > 0.01 / 304.8) values.Add(number);
        return values.ToArray();
    }

    public static int Cut(Document doc, Wall finish, FinishTarget target, double baseZ, double topZ)
    {
        if (target.OpeningError != null) throw new InvalidOperationException(target.OpeningError);
        Curve center = ((LocationCurve)finish.Location).Curve;
        XYZ start = center.GetEndPoint(0), end = center.GetEndPoint(1);
        XYZ direction = (end - start).Normalize();
        double length = center.Length;
        int count = 0;
        foreach (FinishOpeningRectangle cut in target.Openings)
        {
            double left = Math.Max(0, cut.Left), right = Math.Min(length, cut.Right);
            double bottom = Math.Max(baseZ, cut.Bottom), top = Math.Min(topZ, cut.Top);
            if (right - left < doc.Application.ShortCurveTolerance || top - bottom < doc.Application.ShortCurveTolerance) continue;
            XYZ a = start + direction * left, b = start + direction * right;
            doc.Create.NewOpening(finish, new XYZ(a.X, a.Y, bottom), new XYZ(b.X, b.Y, top));
            count++;
        }
        return count;
    }
    private static IEnumerable<XYZ> Corners(BoundingBoxXYZ box)
    {
        foreach (double x in new[] { box.Min.X, box.Max.X })
        foreach (double y in new[] { box.Min.Y, box.Max.Y })
        foreach (double z in new[] { box.Min.Z, box.Max.Z }) yield return box.Transform.OfPoint(new XYZ(x, y, z));
    }
}
