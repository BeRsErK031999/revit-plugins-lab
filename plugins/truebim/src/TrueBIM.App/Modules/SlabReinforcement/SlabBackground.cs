#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabBackground(Floor floor, SlabGeometry geometry, SlabOptions options, int direction)
{
    private sealed record Layout(AreaReinforcement Area, double[] Stations, double Width, IList<Curve>? Outline = null)
    {
        internal Dictionary<(AreaReinforcementLayerType Layer, double Row), int> Indices { get; } = new();
    }
    private readonly Document doc = floor.Document;
    private XYZ Along => direction == 0 ? geometry.X : geometry.Y;
    private XYZ Across => geometry.N.CrossProduct(Along);
    private double Cover => options.EndCover * Mm;

    private static readonly AreaReinforcementLayerType[] MajorLayers =
    {
        AreaReinforcementLayerType.TopOrFrontMajor,
        AreaReinforcementLayerType.BottomOrBackMajor
    };
    internal SlabShortSpans ShortSpans { get; } = new(geometry, options, direction);

    internal IList<AreaReinforcement> Create()
    {
        var range = geometry.Range(Across);
        var stations = SlabLayoutRules.Stations((range.Max - range.Min) / Mm, options.Step).Select(s => range.Min + s * Mm).ToArray();
        var whole = NewArea(null);
        var wholeLayout = new Layout(whole, stations, range.Max - range.Min);
        Configure(new[] { wholeLayout });
        if (Problem(whole) == null)
            return ReplaceShortSpans(new[] { wholeLayout });
        doc.Delete(whole.Id);
        var bands = SlabBands.Profiles(geometry, Across, stations, Cover).Select(profile =>
        {
            var extent = geometry.Range(Across, profile.Outline);
            return new Layout(NewArea(profile.Outline), profile.Rows, extent.Max - extent.Min, profile.Outline);
        }).ToList();
        if (bands.Count == 0)
            throw new InvalidOperationException("В границах плиты нет доступных рядов.");
        Configure(bands);
        foreach (var band in bands)
            if (Problem(band.Area, band.Outline)is string reason)
                throw new InvalidOperationException("Не удалось построить фон с указанным слоем. " + reason);
        return ReplaceShortSpans(bands);
    }

    private sealed record Span(double Start, double End, double Row, bool Upper);
    private List<Span> Physical(AreaReinforcement area)
    {
        var result = new List<Span>();
        foreach (var id in area.GetRebarInSystemIds())
        {
            var bars = (RebarInSystem)doc.GetElement(id);
            for (int i = 0; i < bars.NumberOfBarPositions; i++)
            {
                if (!bars.DoesBarExistAtPosition(i)) continue;
                var lines = bars.GetTransformedCenterlineCurves(false, true, true, i);
                if (lines.Count != 1 || lines[0] is not Line line)
                    throw new InvalidOperationException("Для фона требуется прямая форма 000.");
                var p = line.GetEndPoint(0);
                double a = geometry.Coord(p, Along), b = geometry.Coord(line.GetEndPoint(1), Along);
                double depth = -geometry.Coord(p, geometry.N);
                bool upper = Math.Abs(depth - options.Depths[direction] * Mm) < Math.Abs(depth - geometry.Thickness + options.Depths[direction + 2] * Mm);
                result.Add(new(Math.Min(a, b), Math.Max(a, b), geometry.Coord(p, Across), upper));
            }
        }
        return result;
    }

    private IList<AreaReinforcement> ReplaceShortSpans(IList<Layout> layouts)
    {
        double leg = options.Leg(options.EndType(direction));
        bool IsShort(Span s) => SlabLayoutRules.IsShortSpan((s.End - s.Start) / Mm, leg);
        var kept = new List<AreaReinforcement>();
        var splitRows = new List<Span>();
        var expected = new List<Span>();
        foreach (var layout in layouts)
        {
            var actual = Physical(layout.Area);
            expected.AddRange(actual.Where(s => !IsShort(s)));
            var rows = actual.GroupBy(s => Math.Round(s.Row / Tolerance)).ToList();
            var change = rows.Where(row => row.Any(IsShort)).ToList();
            if (change.Count == 0)
            {
                kept.Add(layout.Area);
                continue;
            }
            foreach (var row in change)
            {
                var top = row.Where(s => s.Upper).OrderBy(s => s.Start).ToList();
                var bottom = row.Where(s => !s.Upper).OrderBy(s => s.Start).ToList();
                if (top.Count != bottom.Count || top.Where((s, i) => !SameSpan(s, bottom[i]) || IsShort(s) != IsShort(bottom[i])).Any())
                    throw new InvalidOperationException($"В ряду {row.First().Row / Mm:F1} мм короткий участок имеет разные границы сверху и снизу. Проверьте форму плиты.");
                double target = layout.Stations.OrderBy(s => Math.Abs(s - row.First().Row)).First();
                if (Math.Abs(target - row.First().Row) > Tolerance)
                    throw new InvalidOperationException("Не удалось определить линию короткого участка в области.");
                foreach (var span in top)
                {
                    if (IsShort(span)) ShortSpans.Plan(span.Start, span.End, target);
                    else splitRows.Add(span);
                }
                if (change.Count < rows.Count)
                    foreach (var layer in MajorLayers)
                        layout.Area.SetLineIncluded(false, layer, layout.Indices[(layer, target)]);
            }
            if (change.Count == rows.Count) doc.Delete(layout.Area.Id);
            else kept.Add(layout.Area);
        }
        if (ShortSpans.Stirrups.Count == 0 && ShortSpans.Empty == 0) return kept;
        var replacements = RetainedAreas(splitRows).ToList();
        if (replacements.Count > 0) Configure(replacements);
        else doc.Regenerate();
        kept.AddRange(replacements.Select(l => l.Area));
        expectedBackground = expected;
        return kept;
    }

    private List<Span>? expectedBackground;

    internal void CheckRetained(IList<AreaReinforcement> areas)
    {
        if (expectedBackground == null) return;
        // Revit can change a native row at a tangent opening. Compare both physical layers
        // after the host has solved its constraints, including the untouched side of each opening.
        var observed = areas.SelectMany(Physical).ToList();
        foreach (var span in expectedBackground)
        {
            int index = observed.FindIndex(s => s.Upper == span.Upper && SameSpan(s, span));
            if (index < 0)
                throw new InvalidOperationException($"Revit изменил длинный стержень в ряду {span.Row / Mm:F1} мм. Операция отменена.");
            observed.RemoveAt(index);
        }
        if (observed.Count > 0) throw new InvalidOperationException("В раскладке остались лишние фоновые стержни. Операция отменена.");
    }

    private static bool SameSpan(Span a, Span b) => Math.Abs(a.Row - b.Row) <= Tolerance &&
        Math.Abs(a.Start - b.Start) <= Tolerance && Math.Abs(a.End - b.End) <= Tolerance;

    private IEnumerable<Layout> RetainedAreas(List<Span> spans)
    {
        // Reuse one editable area for neighbouring rows with coincident ends when the full
        // rectangle between them fits the concrete. A change in the opening starts a new area.
        double half = Math.Min(Cover, 20 * Mm) / 2;
        XYZ Point(double along, double across) => geometry.Origin + Along * along + Across * across;
        XYZ[] Rectangle(Span span, double first, double last) => new[] {
            Point(span.Start, first - half), Point(span.End, first - half),
            Point(span.End, last + half), Point(span.Start, last + half) };
        foreach (var group in spans.GroupBy(s => (Math.Round(s.Start / Mm, 3), Math.Round(s.End / Mm, 3))))
        {
            var sorted = group.OrderBy(s => s.Row).ToList();
            for (int i = 0; i < sorted.Count;)
            {
                int next = i + 1;
                while (next < sorted.Count && sorted[next].Row - sorted[next - 1].Row <= options.Step * Mm + Tolerance &&
                    geometry.FootprintInside(Rectangle(sorted[i], sorted[next - 1].Row, sorted[next].Row)))
                    next++;
                var rows = sorted.GetRange(i, next - i).Select(s => s.Row).ToArray();
                double last = rows[rows.Length - 1];
                var corners = Rectangle(sorted[i], rows[0], last);
                var outline = Enumerable.Range(0, 4).Select(j => (Curve)Line.CreateBound(corners[j], corners[(j + 1) % 4])).ToList();
                yield return new(NewArea(outline), rows, last - rows[0] + 2 * half, outline);
                i = next;
            }
        }
    }

    private AreaReinforcement NewArea(IList<Curve>? outline)
    {
        var type = doc.GetDefaultElementTypeId(ElementTypeGroup.AreaReinforcementType);
        if (type == ElementId.InvalidElementId)
            throw new InvalidOperationException("В проекте отсутствует тип армирования по площади.");
        var bar = options.Bars[direction].Id;
        return outline == null ? AreaReinforcement.Create(doc, floor, Along, type, bar, ElementId.InvalidElementId) : AreaReinforcement.Create(doc, floor, outline, Along, type, bar, ElementId.InvalidElementId);
    }

    private void Configure(IList<Layout> layouts)
    {
        // Parameter activation and line references depend on regeneration. Regenerate once
        // per phase for every area in a direction, including on slabs with many openings.
        doc.Regenerate();
        foreach (var layout in layouts)
        {
            var area = layout.Area;
            area.SetLayerActive(false, AreaReinforcementLayerType.TopOrFrontMinor);
            area.SetLayerActive(false, AreaReinforcementLayerType.BottomOrBackMinor);
            foreach (var layer in MajorLayers)
                area.SetLayerActive(true, layer);
            area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_TOP_DIR_1).Set(options.Bars[direction].Id);
            area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_BAR_TYPE_BOTTOM_DIR_1).Set(options.Bars[direction + 2].Id);
            // The area layout rule uses its own integer values. 3 is maximum spacing.
            area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_LAYOUT_RULE).Set(3);
        }

        doc.Regenerate();
        foreach (var layout in layouts)
        {
            SetSpacing(layout.Area, options.Step * Mm);
            layout.Area.AdditionalTopCoverOffset = Offset(BuiltInParameter.CLEAR_COVER_TOP, direction);
            layout.Area.AdditionalBottomCoverOffset = Offset(BuiltInParameter.CLEAR_COVER_BOTTOM, direction + 2);
        }

        doc.Regenerate();
        var undersized = layouts.Where(l => MajorLayers.Any(layer => l.Area.GetNumberOfLines(layer) < l.Stations.Length)).ToList();
        foreach (var layout in undersized)
            SetSpacing(layout.Area, layout.Width / (layout.Stations.Length + 2));
        if (undersized.Count > 0)
            doc.Regenerate();
        foreach (var layout in layouts)
            foreach (var layer in MajorLayers)
                PositionLines(layout, layer);
        doc.Regenerate();
    }

    private double Offset(BuiltInParameter coverParameter, int index)
    {
        var cover = (RebarCoverType)doc.GetElement(floor.get_Parameter(coverParameter).AsElementId());
        return options.Depths[index] * Mm - options.Bars[index].BarModelDiameter / 2 - cover.CoverDistance;
    }

    private static void SetSpacing(AreaReinforcement area, double spacing)
    {
        area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_SPACING_TOP_DIR_1).Set(spacing);
        area.get_Parameter(BuiltInParameter.REBAR_SYSTEM_SPACING_BOTTOM_DIR_1).Set(spacing);
    }

    private void PositionLines(Layout layout, AreaReinforcementLayerType layer)
    {
        var area = layout.Area;
        var rows = layout.Stations;
        int count = area.GetNumberOfLines(layer);
        if (count < rows.Length)
            throw new InvalidOperationException($"Revit создал {count} рядов вместо {rows.Length}.");
        var lines = Enumerable.Range(0, count).Select(i => (Index: i, Line: area.GetLineFromLayerAtIndex(layer, i))).Where(p => p.Line != null).ToList();
        if (lines.Count == 0)
            throw new InvalidOperationException("Недостаточно линий для привязки раскладки.");
        var first = lines.First();
        var last = lines.Last();
        double origin = geometry.Coord(first.Line.Origin, Across);
        double pitch = last.Index == first.Index ? 0 : (geometry.Coord(last.Line.Origin, Across) - origin) / (last.Index - first.Index);
        for (int i = 0; i < count; i++)
        {
            if (i >= rows.Length)
            {
                area.SetLineIncluded(false, layer, i);
                continue;
            }

            double target = rows[pitch >= 0 ? i : rows.Length - 1 - i];
            layout.Indices[(layer, target)] = i;
            double delta = target - (origin + (i - first.Index) * pitch);
            if (Math.Abs(delta) > Tolerance)
                area.MoveLine(Across * delta, layer, i);
        }
    }

    private string? Problem(AreaReinforcement area, IList<Curve>? outline = null)
    {
        var ids = area.GetRebarInSystemIds();
        if (ids.Count == 0)
            return "Область не содержит физических стержней.";
        foreach (var id in ids)
        {
            var bars = (RebarInSystem)doc.GetElement(id);
            for (int i = 0; i < bars.NumberOfBarPositions; i++)
            {
                if (!bars.DoesBarExistAtPosition(i))
                    continue;
                foreach (var curve in bars.GetTransformedCenterlineCurves(false, true, true, i))
                {
                    var p = curve.GetEndPoint(0);
                    var q = curve.GetEndPoint(1);
                    if (!geometry.SegmentInside(p, q))
                        return "Стержень пересекает вырез.";
                    foreach (var end in new[]
                    {
                        p,
                        q
                    })
                    {
                        double distance = geometry.EndClearance(end, Cover + 1 * Mm);
                        double sketchDistance = outline?.Min(c => c.Distance(geometry.OnTop(end))) ?? double.PositiveInfinity;
                        if (distance < Cover - 0.5 * Mm || (distance > Cover + 0.5 * Mm && sketchDistance > 0.5 * Mm))
                            return $"Торец стержня {id} в ряду {geometry.Coord(end, Across)/Mm:F1} мм, вдоль оси {geometry.Coord(end, Along)/Mm:F1} мм. Отклонение от контура области {sketchDistance/Mm:F2} мм. Расстояние до грани {geometry.EndClearance(end, 1000*Mm)/Mm:F2} мм при заданных {options.EndCover:0.##} мм.";
                    }
                }
            }
        }

        return null;
    }
}
#endif
