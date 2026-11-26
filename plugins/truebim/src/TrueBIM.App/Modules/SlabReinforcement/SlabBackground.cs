#if REVIT2024_OR_GREATER && NET48
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using static TrueBIM.App.Modules.SlabReinforcement.SlabGeometry;

namespace TrueBIM.App.Modules.SlabReinforcement;

internal sealed class SlabBackground(Floor floor, SlabGeometry geometry, SlabOptions options, int direction)
{
    private sealed record Layout(AreaReinforcement Area, double[] Stations, double Width, IList<Curve>? Outline = null);
    private readonly Document doc = floor.Document;
    private XYZ Along => direction == 0 ? geometry.X : geometry.Y;
    private XYZ Across => geometry.N.CrossProduct(Along);
    private double Cover => options.EndCover * Mm;

    private static readonly AreaReinforcementLayerType[] MajorLayers =
    {
        AreaReinforcementLayerType.TopOrFrontMajor,
        AreaReinforcementLayerType.BottomOrBackMajor
    };
    internal IList<AreaReinforcement> Create()
    {
        var range = geometry.Range(Across);
        var stations = SlabLayoutRules.Stations((range.Max - range.Min) / Mm, options.Step).Select(s => range.Min + s * Mm).ToArray();
        var whole = NewArea(null);
        Configure(new[] { new Layout(whole, stations, range.Max - range.Min) });
        if (Problem(whole) == null)
            return new[]
            {
                whole
            };
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
        return bands.Select(b => b.Area).ToList();
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
