using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private sealed class ChainReference
    {
        public XYZ Point { get; set; } = null!;
        public DetailingAxis? Axis { get; set; }
        public DetailingFace? Face { get; set; }
        public string Run { get; set; } = "";
    }

    private static DetailingRect Region(DetailingFrame frame, IEnumerable<DetailingBar> bars)
    {
        var points = bars.SelectMany(b => b.Segments.SelectMany(s => new[] { s.Start, s.End }).Concat(b.Positions)).ToList();
        return new DetailingRect(points.Min(frame.X), points.Min(frame.Y), points.Max(frame.X), points.Max(frame.Y));
    }

    private static void CombinedDimensions(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry,
        DetailingOptions options, DetailingResult result)
    {
        if (options.BarDimensions && geometry.Bars.Any(b => b.Link != null))
            result.Notes.Add("Арматура связей: марки расставлены; размерные цепочки создаются для арматуры текущего проекта.");
        foreach (var section in Sections(frame, geometry, options))
        {
            var bars = section.Bars.Where(b => b.Link == null).ToList();
            var bounds = section.Reinforcement;
            var layout = SectionAnnotationBoundary(doc, view, frame, section);
            foreach (bool horizontal in new[] { true, false })
            {
                XYZ direction = horizontal ? frame.Right : frame.Up;
                double Coordinate(XYZ point) => horizontal ? frame.X(point) : frame.Y(point);
                var faces = section.Faces.Where(f => f.Horizontal == horizontal).OrderBy(f => Coordinate(f.Point)).ToList();
                if (options.BarDimensions)
                {
                    var entries = bars.Where(bar => !IsPin(bar)).SelectMany(bar => bar.DimensionReferences
                        .Where(axis => horizontal ? Math.Abs(axis.Direction.DotProduct(frame.Up)) > 0.999
                            : Math.Abs(axis.Direction.DotProduct(frame.Normal)) > 0.999 || Math.Abs(axis.Direction.DotProduct(frame.Right)) > 0.999)
                        .Select(axis => new ChainReference { Point = axis.Point, Axis = axis, Run = bar.Element.GetTypeId() + "|" + bar.Shape }))
                        .OrderBy(p => Coordinate(p.Point)).ToList();
                    if (!horizontal && faces.Count >= 2)
                        entries.RemoveAll(p => Coordinate(p.Point) < Coordinate(faces.First().Point) - 0.1 / 304.8
                            || Coordinate(p.Point) > Coordinate(faces.Last().Point) + 0.1 / 304.8);
                    if (entries.Count >= 1)
                    {
                        double low = Coordinate(entries.First().Point), high = Coordinate(entries.Last().Point);
                        var start = faces.Where(f => Coordinate(f.Point) < low - 0.1 / 304.8).LastOrDefault();
                        var end = faces.FirstOrDefault(f => Coordinate(f.Point) > high + 0.1 / 304.8);
                        // Covers are measured from the real wall faces, including the
                        // first and last transverse bar. Crop edges supply no references.
                        if (start != null && low - Coordinate(start.Point) < 500 / 304.8)
                            entries.Insert(0, new ChainReference { Point = start.Point, Face = start });
                        if (end != null && Coordinate(end.Point) - high < 500 / 304.8)
                            entries.Add(new ChainReference { Point = end.Point, Face = end });
                        XYZ origin = horizontal ? frame.Point(low, layout.Bottom - frame.Paper(8)) : frame.Point(layout.Left - frame.Paper(8), low);
                        AddChain(entries, origin, direction, horizontal, !horizontal);
                    }
                }
                if (!options.ConcreteDimensions) continue;
                var concrete = faces.Select(f => new ChainReference { Point = f.Point, Face = f }).ToList();
                if (!horizontal)
                {
                    // Different slab heights on the two sides use the outside
                    // levels, without stacking intermediate duplicate elevations.
                    var slabKeys = new HashSet<string>(section.UpperSlabs.Select(h => h.Key));
                    var slabLevels = geometry.Faces.Where(f => !f.Horizontal && slabKeys.Contains(f.HostKey)).OrderBy(f => frame.Y(f.Point)).ToList();
                    if (slabLevels.Count > 0)
                    {
                        double low = frame.Y(slabLevels.First().Point), high = frame.Y(slabLevels.Last().Point);
                        concrete.RemoveAll(p => frame.Y(p.Point) > low + 0.1 / 304.8 && frame.Y(p.Point) < high - 0.1 / 304.8);
                        concrete.Add(new ChainReference { Point = slabLevels.First().Point, Face = slabLevels.First() });
                        concrete.Add(new ChainReference { Point = slabLevels.Last().Point, Face = slabLevels.Last() });
                    }
                    double highest = concrete.Count == 0 ? double.PositiveInfinity : concrete.Max(p => frame.Y(p.Point));
                    var exposed = bars.SelectMany(b => b.EndReferences).Where(a => frame.Y(a.Point) > highest + 1 / 304.8)
                        .OrderByDescending(a => frame.Y(a.Point)).FirstOrDefault();
                    if (exposed != null) concrete.Add(new ChainReference { Point = exposed.Point, Axis = exposed });
                }
                XYZ concreteOrigin = horizontal ? frame.Point(bounds.Left, layout.Bottom - frame.Paper(options.BarDimensions ? 16 : 8))
                    : frame.Point(layout.Left - frame.Paper(options.BarDimensions ? 16 : 8), bounds.Bottom);
                AddChain(concrete, concreteOrigin, direction, horizontal, false);
            }
            if (options.BarDimensions && options.Variant != DetailingVariant.Straight)
            {
                // The top row measures the bent ends against their vertical legs
                // and the wall faces; opposing bends include both sides in one row.
                var bent = bars.SelectMany(b => b.BendEndReferences.Select(a => new ChainReference { Point = a.Point, Axis = a, Run = b.Shape }))
                    .Concat(bars.SelectMany(b => b.DimensionReferences.Where(a => Math.Abs(a.Direction.DotProduct(frame.Up)) > 0.999)
                        .Select(a => new ChainReference { Point = a.Point, Axis = a, Run = b.Shape })))
                    .Concat(section.Faces.Where(f => f.Horizontal).Select(f => new ChainReference { Point = f.Point, Face = f })).ToList();
                if (bars.Any(b => b.BendEndReferences.Count > 0))
                {
                    double row = Math.Max(bounds.Top, section.Wall.Top) + frame.Paper(8);
                    // Keep existing manual slab notes intact and place the new
                    // top dimension above them if they occupy its normal row.
                    var notes = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType()
                        .Where(e => (e is AnnotationSymbol || e is TextNote) && !OwnedDetail(e))
                        .Select(e => e.get_BoundingBox(view)).Where(b => b != null).Select(b =>
                        {
                            var corners = new List<XYZ>();
                            for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                                corners.Add(b!.Transform.OfPoint(new XYZ(x == 0 ? b.Min.X : b.Max.X,
                                    y == 0 ? b.Min.Y : b.Max.Y, z == 0 ? b.Min.Z : b.Max.Z)));
                            return new DetailingRect(corners.Min(frame.X), corners.Min(frame.Y), corners.Max(frame.X), corners.Max(frame.Y));
                        }).OrderBy(b => b.Bottom).ToList();
                    foreach (var note in notes)
                        if (note.Right >= bent.Min(p => frame.X(p.Point)) && note.Left <= bent.Max(p => frame.X(p.Point))
                            && note.Top >= row - frame.Paper(3) && note.Bottom <= row + frame.Paper(3))
                            row = note.Top + frame.Paper(4);
                    AddChain(bent, frame.Point(bounds.Left, row), frame.Right, true, false);
                }
            }

            void AddChain(List<ChainReference> entries, XYZ origin, XYZ direction, bool horizontal, bool equality)
            {
                var plan = DetailingChainPlan.Create(entries.Select((p, i) => new DetailingChainPoint(i,
                    (horizontal ? frame.X(p.Point) : frame.Y(p.Point)) * 304.8, p.Run, p.Face != null)), equality);
                if (plan.Points.Count < 2) return;
                result.LayoutTrace.Add("chain " + (horizontal ? "horizontal" : "vertical") + ": "
                    + string.Join("; ", plan.Sections.Select(s => (s.UseEqualityFormula ? "formula " : "numeric ")
                        + string.Join(",", s.Points.Select(p => p.PositionMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + (p.Boundary ? " face" : ""))))));
                var anchorFace = entries.Select(p => p.Face).FirstOrDefault(f => f != null);
                var axes = entries.Where(p => p.Axis != null).Select(p => p.Axis!).ToList();
                var references = new Dictionary<int, Reference>();
                foreach (var point in plan.Points)
                {
                    ChainReference item = entries[point.Id];
                    var anchor = axes.OrderByDescending(a => Math.Abs((a.Point - item.Point).DotProduct(direction))).FirstOrDefault();
                    references[point.Id] = item.Face?.Reference ?? ResolveAxis(doc, view, item.Axis!, anchorFace, origin, direction, options.DimensionTypeId, anchor);
                }
                foreach (var part in plan.Sections)
                {
                    ElementId type = part.UseEqualityFormula ? EqualityType(doc, options.DimensionTypeId).Id : options.DimensionTypeId;
                    Dimension dimension = CreateDimension(doc, view, origin, direction, part.Points.Select(p => references[p.Id]), type);
                    doc.Regenerate();
                    if (part.UseEqualityFormula)
                    {
                        var segments = dimension.Segments.Cast<DimensionSegment>().ToList();
                        if (segments.Count != part.Intervals || segments.Any(s => !s.Value.HasValue || Math.Abs(s.Value.Value * 304.8 - part.PitchMm) > 0.1))
                            throw new InvalidOperationException("Revit не подтвердил равный шаг размерной цепочки. Изменения вида отменены.");
                        Parameter display = dimension.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ);
                        if (display == null || display.IsReadOnly || !display.Set(2))
                            throw new InvalidOperationException("Выбранный размер не поддерживает отображение формулы равенства.");
                        doc.Regenerate();
                        if (display.AsInteger() != 2 || dimension.AreSegmentsEqual)
                            throw new InvalidOperationException("Не удалось включить формулу равенства без ограничения геометрии.");
                    }
                    result.Dimensions++;
                }
            }
        }
    }

    private static DimensionType EqualityType(Document doc, ElementId sourceId)
    {
        DimensionType source = (DimensionType)doc.GetElement(sourceId);
        if (!source.CanHaveEqualityFormula()) throw new InvalidOperationException("Для формулы равенства выберите непрерывный линейный размерный стиль.");
        string name = source.Name + " · TrueBIM равный шаг";
        string role = "equality-style:" + source.UniqueId;
        var existing = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>().ToList();
        DimensionType? own = existing.FirstOrDefault(t => DetailRole(t) == role);
        if (own != null)
        {
            own.get_Parameter(BuiltInParameter.EQUALITY_WITNESS_DISPLAY)?.Set(2);
            return own;
        }
        string available = name;
        for (int index = 2; existing.Any(t => t.Name == available); index++) available = name + " " + index;
        DimensionType type = (DimensionType)source.Duplicate(available);
        using FormatOptions format = source.GetUnitsFormatOptions();
        type.SetEqualityFormula(new List<DimensionEqualityLabelFormatting>
        {
            new(0, "", LabelType.NumberOfSegments, "×", new FormatOptions()),
            new(0, "", LabelType.LengthOfSegment, "=", format),
            new(0, "", LabelType.TotalLength, "", format)
        });
        // Hide intermediate ticks while retaining their real references.
        type.get_Parameter(BuiltInParameter.EQUALITY_WITNESS_DISPLAY)?.Set(2);
        MarkDetail(type, role);
        return type;
    }

    private static bool IsPin(DetailingBar bar) => (bar.Element.LookupParameter("• Деталь • Арматура. Префикс")?.AsString() ?? "") == "Ш"
        || bar.ShapeName.IndexOf("Ш1", StringComparison.OrdinalIgnoreCase) >= 0;
}
