#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal static class SlabFrames
{
    internal static List<FamilyInstance> Place(Floor floor, SlabGeometry g, SlabOptions o, Line first, Line second, List<string> warnings)
    {
        var d = floor.Document;
        var symbol = o.FrameType!;
        if (symbol.Family.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
            throw new InvalidOperationException("Выберите семейство каркаса с размещением на рабочей плоскости.");
        XYZ x = g.X, y = g.Y;
        XYZ p1 = g.OnTop(first.GetEndPoint(0)), p2 = g.OnTop(second.GetEndPoint(0));
        if (Math.Abs(second.Direction.DotProduct(x)) > .001)
            throw new InvalidOperationException("Для каркасов нужны две взаимно перпендикулярные прямые кромки.");
        XYZ origin = g.Origin + x * g.Coord(p2, x) + y * g.Coord(p1, y);
        // Either sign of the selected edge is valid. The concrete side points into the grid.
        XYZ Inward(Line edge, XYZ normal) => g.Inside(g.OnTop(edge.Evaluate(.5, true)) + normal * (2 * Mm)) ? normal : -normal;
        x = Inward(second, x);
        y = Inward(first, y);
        var xr = g.Range(x);
        var yr = g.Range(y);
        double dx = o.FrameX * Mm, dy = o.FrameY * Mm;
        double maxX = xr.Max - g.Coord(origin, x), maxY = yr.Max - g.Coord(origin, y);
        var positions = new List<XYZ>();
        for (double row = dy / 2; row < maxY; row += dy)
        {
            XYZ start = origin + y * row;
            foreach (double col in Stations(g, start, x, maxX, dx))
            {
                var column = Stations(g, origin + x * col, y, maxY, dy);
                double rowAt = column.Where(v => Math.Abs(v - row) <= dy / 2 + Tolerance).OrderBy(v => Math.Abs(v - row)).DefaultIfEmpty(double.NaN).First();
                if (!double.IsNaN(rowAt))
                    positions.Add(origin + x * col + y * rowAt);
            }
        }

        if (positions.Count == 0)
        {
            warnings.Add("В границах плиты нет позиций для каркасов.");
            return new();
        }

        if (!symbol.IsActive)
        {
            symbol.Activate();
            d.Regenerate();
        }

        XYZ longAxis = o.Depths[2] <= o.Depths[3] ? y : x;
        var reference = HostObjectUtils.GetTopFaces(floor).Single();
        var seed = d.Create.NewFamilyInstance(reference, positions[0], longAxis, symbol);
        d.Regenerate();
        var vertices = new List<XYZ>();
        CollectFrame(seed, vertices);
        if (vertices.Count == 0)
            throw new InvalidOperationException("В выбранном типе каркаса нет геометрии для проверки габаритов.");
        // Some families are authored along local Y. Align the actual longer footprint.
        XYZ across = g.N.CrossProduct(longAxis);
        if (Span(vertices, longAxis) < Span(vertices, across))
        {
            d.Delete(seed.Id);
            longAxis = across;
            seed = d.Create.NewFamilyInstance(reference, positions[0], longAxis, symbol);
            d.Regenerate();
            vertices.Clear();
            CollectFrame(seed, vertices);
        }

        double baseOffset = -g.Thickness + o.EndCover * Mm - vertices.Min(v => g.Coord(v, g.N));
        if (vertices.Max(v => g.Coord(v, g.N)) + baseOffset > 0.1 * Mm)
            throw new InvalidOperationException($"Каркас «{symbol.Name}» имеет высоту {Span(vertices, g.N) / Mm:F0} мм. При толщине плиты {g.Thickness / Mm:F0} мм и слое {o.EndCover:F0} мм доступно {g.Thickness / Mm - o.EndCover:F0} мм. Выберите другой тип или отключите каркасы.");
        var offset = seed.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
        if (offset == null || offset.IsReadOnly)
            throw new InvalidOperationException("Семейство каркаса должно допускать смещение от основы.");
        double value = offset.AsDouble() + baseOffset;
        var firstPoint = positions[0];
        double x0 = vertices.Min(v => (v - firstPoint).DotProduct(x)), x1 = vertices.Max(v => (v - firstPoint).DotProduct(x));
        double y0 = vertices.Min(v => (v - firstPoint).DotProduct(y)), y1 = vertices.Max(v => (v - firstPoint).DotProduct(y));
        d.Delete(seed.Id);
        var result = new List<FamilyInstance>();
        var occupied = new HashSet<(long, long)>();
        var anchors = new List<XYZ>();
        int skipped = 0;
        foreach (var p in positions)
        {
            bool placed = false;
            foreach (var delta in new[]
            {
                XYZ.Zero,
                x * (dx / 2),
                -x * (dx / 2),
                y * (dy / 2),
                -y * (dy / 2)
            })
            {
                var at = p + delta;
                var key = ((long)Math.Round(g.Coord(at, x) / Mm * 10), (long)Math.Round(g.Coord(at, y) / Mm * 10));
                if (occupied.Contains(key))
                {
                    if (delta.IsZeroLength())
                    {
                        placed = true;
                        break;
                    }

                    continue;
                }

                var corners = new[]
                {
                    at + x * x0 + y * y0,
                    at + x * x1 + y * y0,
                    at + x * x1 + y * y1,
                    at + x * x0 + y * y1
                };
                if (anchors.Any(previous => Math.Abs((at - previous).DotProduct(x)) < x1 - x0 - Tolerance &&
                                            Math.Abs((at - previous).DotProduct(y)) < y1 - y0 - Tolerance))
                    continue;
                if (!g.FootprintInside(corners))
                    continue;
                var inst = d.Create.NewFamilyInstance(reference, at, longAxis, symbol);
                inst.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM).Set(value);
                var mark = inst.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
                if (mark == null || mark.IsReadOnly)
                    throw new InvalidOperationException("У каркаса нет доступной марки экземпляра.");
                mark.Set(o.Mark);
                result.Add(inst);
                occupied.Add(key);
                anchors.Add(at);
                placed = true;
                break;
            }

            if (!placed)
                skipped++;
        }

        if (skipped > 0)
            warnings.Add($"У препятствий пропущено позиций каркасов: {skipped}.");
        return result;
    }

    private static List<double> Stations(SlabGeometry g, XYZ origin, XYZ axis, double extent, double step)
    {
        var result = new List<double>();
        double anchor = step / 2, previous = 0;
        foreach (var(a, b)in g.Intervals(origin, axis, extent))
        {
            if (a - previous > step / 2 + Tolerance)
                anchor = a + step / 2;
            double first = anchor + Math.Max(0, Math.Ceiling((a - anchor - Tolerance) / step)) * step;
            for (double p = first; p < b - Tolerance; p += step)
                result.Add(p);
            previous = b;
        }

        return result;
    }

    private static double Span(IEnumerable<XYZ> points, XYZ axis) => points.Max(p => p.DotProduct(axis)) - points.Min(p => p.DotProduct(axis));
    private static void CollectFrame(FamilyInstance family, List<XYZ> points)
    {
        var geometry = family.get_Geometry(new Options { DetailLevel = ViewDetailLevel.Fine });
        if (geometry != null)
            CollectVertices(geometry, points);
        foreach (var id in family.GetSubComponentIds())
            if (family.Document.GetElement(id)is FamilyInstance child)
                CollectFrame(child, points);
    }

    private static void CollectVertices(GeometryElement geometry, List<XYZ> points)
    {
        foreach (var item in geometry)
        {
            if (item is GeometryInstance gi)
                CollectVertices(gi.GetInstanceGeometry(), points);
            if (item is Solid s && s.Volume > 0)
                foreach (Edge edge in s.Edges)
                    points.AddRange(edge.Tessellate());
        }
    }
}
#endif
