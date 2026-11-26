#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal static class SlabEnds
{
    private sealed record Span(XYZ First, XYZ Last, ElementId BarId);
    private sealed record End(XYZ Top, XYZ Bottom, int Sign, int Side, double Station);
    internal static IList<Rebar> Create(Floor floor, SlabGeometry g, SlabOptions o, IEnumerable<AreaReinforcement> areas, int direction, RebarShape straight, RebarShape bend, List<string> warnings, HashSet<ElementId> review, List<SlabEndCheck> checks)
    {
        var d = floor.Document;
        XYZ axis = direction == 0 ? g.X : g.Y, cross = g.N.CrossProduct(axis);
        var topType = o.Bars[direction];
        var bottomType = o.Bars[direction + 2];
        var endType = topType.BarNominalDiameter >= bottomType.BarNominalDiameter ? topType : bottomType;
        double leg = o.Leg(endType) * Mm, radius = endType.BarModelDiameter / 2;
        var rows = new SortedDictionary<long, (List<Span> Upper, List<Span> Lower)>();
        foreach (var area in areas)
            foreach (var id in area.GetRebarInSystemIds())
            {
                var bars = (RebarInSystem)d.GetElement(id);
                if (bars.RebarShapeId != straight.Id)
                    throw new InvalidOperationException($"Область {area.Id} создала форму {d.GetElement(bars.RebarShapeId)?.Name}. В проекте нужна системная прямая форма {straight.Name}.");
                for (int i = 0; i < bars.NumberOfBarPositions; i++)
                {
                    if (!bars.DoesBarExistAtPosition(i))
                        continue;
                    var curves = bars.GetTransformedCenterlineCurves(false, true, true, i);
                    if (curves.Count != 1 || curves[0] is not Line line)
                        throw new InvalidOperationException("Для торцов требуется прямолинейный фоновый стержень.");
                    XYZ p = line.GetEndPoint(0), q = line.GetEndPoint(1);
                    if ((q - p).DotProduct(axis) < 0)
                        (p, q) = (q, p);
                    double z = -g.Coord(p, g.N), upper = o.Depths[direction] * Mm, lower = g.Thickness - o.Depths[direction + 2] * Mm;
                    bool isUpper = Math.Abs(z - upper) < Math.Abs(z - lower);
                    if (Math.Abs(z - (isUpper ? upper : lower)) > 0.2 * Mm)
                        throw new InvalidOperationException($"Ось стержня {id} на глубине {z / Mm:F2} мм вместо {(isUpper ? upper : lower) / Mm:F2} мм.");
                    long key = (long)Math.Round(g.Coord(p, cross) / Mm * 100);
                    if (!rows.TryGetValue(key, out var row))
                        rows.Add(key, row = (new(), new()));
                    (isUpper ? row.Upper : row.Lower).Add(new(p, q, id));
                }
            }

        var ends = new List<End>();
        int shortEnds = 0, unmatched = 0;
        foreach (var row in rows.Values)
        {
            var upper = row.Upper.OrderBy(s => g.Coord(s.First, axis)).ToList();
            var lower = row.Lower.OrderBy(s => g.Coord(s.First, axis)).ToList();
            foreach (var u in upper)
            {
                // Blind cuts or sloped end faces can split the two layers differently.
                foreach (int sign in new[] { 1, -1 })
                {
                    XYZ p = sign == 1 ? u.First : u.Last;
                    var mate = lower.Where(l => (l.Last - u.First).DotProduct(axis) > Tolerance && (u.Last - l.First).DotProduct(axis) > Tolerance).OrderBy(l => Math.Abs(((sign == 1 ? l.First : l.Last) - p).DotProduct(axis))).FirstOrDefault();
                    if (mate == null)
                    {
                        unmatched++;
                        review.Add(u.BarId);
                        continue;
                    }

                    XYZ q = sign == 1 ? mate.First : mate.Last;
                    XYZ inward = axis * sign;
                    // The outside of the web aligns with the covered end of the more recessed bar.
                    double setback = Math.Max(0, (q - p).DotProduct(inward));
                    XYZ web = g.OnTop(p) + inward * (setback + radius);
                    var farTop = sign == 1 ? u.Last : u.First;
                    var farBottom = sign == 1 ? mate.Last : mate.First;
                    double room = Math.Min((farTop - web).DotProduct(inward), (farBottom - web).DotProduct(inward)) - (leg - radius);
                    if (room < -Tolerance)
                    {
                        shortEnds++;
                        review.Add(u.BarId);
                        continue;
                    }

                    bool placed = false;
                    foreach (int side in new[] { 1, -1 })
                    {
                        XYZ top = web + cross * (side * (radius + topType.BarModelDiameter / 2)) + g.N * g.Coord(p, g.N);
                        XYZ bottom = web + cross * (side * (radius + bottomType.BarModelDiameter / 2)) + g.N * g.Coord(q, g.N);
                        double shift = CoverShift(g, top, bottom, inward, o.EndCover * Mm + radius, Math.Max(0, room));
                        if (double.IsNaN(shift))
                            continue;
                        top += inward * shift;
                        bottom += inward * shift;
                        if (!g.SegmentInside(top, top + inward * (leg - radius)) || !g.SegmentInside(bottom, bottom + inward * (leg - radius)))
                            continue;
                        ends.Add(new(top, bottom, sign, side, g.Coord(top, cross)));
                        placed = true;
                        break;
                    }

                    if (!placed)
                    {
                        shortEnds++;
                        review.Add(u.BarId);
                    }
                }
            }
        }

        if (shortEnds > 0)
            warnings.Add($"Направление {direction + 1}: в {shortEnds} местах целая ветвь {leg / Mm:F0} мм выходит из бетона или за пределы фонового стержня.");
        if (unmatched > 0)
            warnings.Add($"Торцы без пары стержней сверху и снизу: {unmatched}.");
        var result = new List<Rebar>();
        // Group only straight, parallel, equally spaced details with the same web position.
        foreach (var group in ends.GroupBy(e => (e.Sign, e.Side, offset: Math.Round(g.Coord(e.Top, axis) / Tolerance))))
        {
            var list = group.OrderBy(e => e.Station).ToList();
            for (int i = 0; i < list.Count;)
            {
                int j = i + 1;
                double pitch = j < list.Count ? list[j].Station - list[i].Station : 0;
                if (pitch > Tolerance && Math.Abs(topType.BarModelDiameter - bottomType.BarModelDiameter) < Tolerance)
                    while (j < list.Count && Math.Abs(list[j].Station - list[i].Station - (j - i) * pitch) < Tolerance && j - i < 1000)
                        j++;
                var e = list[i];
                XYZ arm = axis * (e.Sign * (leg - radius));
                XYZ normal = axis.CrossProduct(e.Bottom - e.Top).Normalize();
                var curves = new List<Curve>
                {
                    Line.CreateBound(e.Top + arm, e.Top),
                    Line.CreateBound(e.Top, e.Bottom),
                    Line.CreateBound(e.Bottom, e.Bottom + arm)
                };
                var bar = Rebar.CreateFromCurvesAndShape(d, bend, endType, null, null, floor, normal, curves, RebarHookOrientation.Left, RebarHookOrientation.Left);
                if (bar == null)
                    throw new InvalidOperationException($"Revit не принял форму 200 у ряда {e.Station / Mm:F0} мм.");
                if (j - i > 1)
                    bar.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(j - i, pitch, normal.DotProduct(cross) > 0, true, true);
                var expected = list.GetRange(i, j - i)
                    .Select(end => new[] { end.Top + arm, end.Top, end.Bottom, end.Bottom + arm }).ToList();
                checks.Add(new SlabEndCheck(bar, expected));
                result.Add(bar);
                i = j;
            }
        }

        return result;
    }

    private static double CoverShift(SlabGeometry geometry, XYZ top, XYZ bottom, XYZ inward, double cover, double room)
    {
        bool Clears(double shift) => new[]
        {
            top,
            bottom
        }.All(p => geometry.Inside(p + inward * shift) && geometry.EndClearance(p + inward * shift, cover + Mm) >= cover - Tolerance);
        if (Clears(0))
            return 0;
        double low = 0, high = Math.Min(room, 5 * Mm);
        while (high > low)
        {
            if (Clears(high))
            {
                while (high - low > Tolerance)
                {
                    double mid = (low + high) / 2;
                    if (Clears(mid))
                        high = mid;
                    else
                        low = mid;
                }

                return high;
            }

            low = high;
            high = Math.Min(room, 2 * high);
        }

        return double.NaN;
    }
}
#endif
