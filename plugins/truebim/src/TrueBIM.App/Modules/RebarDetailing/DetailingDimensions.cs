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
        var local = geometry.Bars.Where(b => b.Link == null).ToList();
        if (options.BarDimensions && geometry.Bars.Any(b => b.Link != null))
            result.Notes.Add("Арматура связей: марки расставлены; размерные цепочки создаются для арматуры текущего проекта.");
        if (local.Count == 0) return;
        var regions = frame.Vertical ? local.GroupBy(b => DetailingGeometry.Key(b.Host, b.Link)).Select(g => g.ToList()).ToList()
            : new List<List<DetailingBar>> { local };
        foreach (var bars in regions)
        {
            var keys = new HashSet<string>(bars.Select(b => DetailingGeometry.Key(b.Host, b.Link)));
            var hostFaces = geometry.Faces.Where(f => keys.Contains(f.HostKey)).ToList();
            DetailingRect bounds = Region(frame, bars);
            double dimensionLeft = bounds.Left - (frame.Vertical && options.Details ? frame.Paper(5) : 0);
            double dimensionBottom = bounds.Bottom;
            if (frame.Vertical && options.Details && hostFaces.Any(f => !f.Horizontal))
            {
                dimensionBottom = Math.Min(dimensionBottom, hostFaces.Where(f => !f.Horizontal).Min(f => frame.Y(f.Point)) - frame.Paper(6));
                double wallBottom = hostFaces.Where(f => !f.Horizontal).Min(f => frame.Y(f.Point));
                var breaks = new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(e => e.Symbol.FamilyName.StartsWith("(Узел) Область обрыва", StringComparison.Ordinal))
                    .Select(e => Bounds(e, view, frame)).Where(b => b.HasValue).Select(b => b!.Value)
                    .Where(b => b.MaxX >= bounds.Left - frame.Paper(10) && b.MinX <= bounds.Right + frame.Paper(10) && b.MinY < wallBottom
                        && b.MaxY > wallBottom - frame.Paper(20) && b.MaxY < wallBottom + frame.Paper(20)).ToList();
                if (breaks.Count > 0) dimensionBottom = Math.Min(dimensionBottom, breaks.Min(b => b.MinY) + frame.Paper(4));
            }
            foreach (bool horizontal in new[] { true, false })
            {
                XYZ direction = horizontal ? frame.Right : frame.Up;
                double Coordinate(XYZ point) => horizontal ? frame.X(point) : frame.Y(point);
                var faces = hostFaces.Where(f => f.Horizontal == horizontal).OrderBy(f => Coordinate(f.Point)).ToList();
                if (options.BarDimensions)
                {
                    // A plan chain crosses every longitudinal bar in the node, including the
                    // adjacent wall. A section chain combines field reinforcement and ties.
                    var entries = bars.Where(bar => !IsPin(bar)).SelectMany(bar => bar.DimensionReferences
                        .Where(axis => Math.Abs(axis.Direction.DotProduct(frame.Vertical && horizontal ? frame.Up : frame.Normal)) > 0.999
                            || (frame.Vertical && !horizontal && Math.Abs(axis.Direction.DotProduct(frame.Right)) > 0.999))
                        .Select(axis => new ChainReference { Point = axis.Point, Axis = axis,
                            Run = bar.Element.GetTypeId() + "|" + bar.Shape })).OrderBy(p => Coordinate(p.Point)).ToList();
                    if (frame.Vertical && !horizontal && faces.Count >= 2)
                        entries.RemoveAll(p => Coordinate(p.Point) < Coordinate(faces.First().Point) - 0.1 / 304.8
                            || Coordinate(p.Point) > Coordinate(faces.Last().Point) + 0.1 / 304.8);
                    if (entries.Count >= 2 && Coordinate(entries.Last().Point) - Coordinate(entries.First().Point) > 1 / 304.8)
                    {
                        double low = Coordinate(entries.First().Point), high = Coordinate(entries.Last().Point);
                        var start = faces.Where(f => Coordinate(f.Point) < low - 1 / 304.8).OrderByDescending(f => Coordinate(f.Point)).FirstOrDefault();
                        var end = faces.FirstOrDefault(f => Coordinate(f.Point) > high + 1 / 304.8);
                        // A cropped end has no concrete reference and must stay open.
                        if (start != null && low - Coordinate(start.Point) < 500 / 304.8)
                            entries.Insert(0, new ChainReference { Point = start.Point, Face = start });
                        if (end != null && Coordinate(end.Point) - high < 500 / 304.8)
                            entries.Add(new ChainReference { Point = end.Point, Face = end });
                        XYZ origin = horizontal ? frame.Point(low, dimensionBottom - frame.Paper(8))
                            : frame.Point(frame.Vertical ? dimensionLeft - frame.Paper(8) : bounds.Right + frame.Paper(8), low);
                        AddChain(entries, origin, direction, horizontal, true);
                    }
                }
                if (!options.ConcreteDimensions) continue;
                if (frame.Vertical)
                {
                    var concrete = faces.Select(f => new ChainReference { Point = f.Point, Face = f }).ToList();
                    if (!horizontal)
                    {
                        // Include slab levels adjoining this wall, without measuring every
                        // unrelated concrete object in the view crop.
                        concrete.AddRange(geometry.Faces.Where(f => !f.Horizontal && !keys.Contains(f.HostKey)
                            && f.Bounds.Right >= bounds.Left - 1 / 304.8 && f.Bounds.Left <= bounds.Right + 1 / 304.8
                            && frame.Y(f.Point) >= bounds.Bottom && frame.Y(f.Point) <= bounds.Top)
                            .Select(f => new ChainReference { Point = f.Point, Face = f }));
                        double highest = concrete.Count == 0 ? double.PositiveInfinity : concrete.Max(p => frame.Y(p.Point));
                        var exposed = bars.SelectMany(b => b.EndReferences).Where(a => frame.Y(a.Point) > highest + 1 / 304.8)
                            .OrderByDescending(a => frame.Y(a.Point)).FirstOrDefault();
                        if (exposed != null) concrete.Add(new ChainReference { Point = exposed.Point, Axis = exposed });
                    }
                    if (concrete.Count >= 2)
                    {
                        XYZ origin = horizontal ? frame.Point(bounds.Left, dimensionBottom - frame.Paper(options.BarDimensions ? 16 : 8))
                            : frame.Point(dimensionLeft - frame.Paper(options.BarDimensions ? 16 : 8), bounds.Bottom);
                        AddChain(concrete, origin, direction, horizontal, false);
                    }
                }
                else
                {
                    var seen = new HashSet<string>();
                    foreach (var host in faces.GroupBy(f => f.HostKey))
                    {
                        var ordered = host.OrderBy(f => Coordinate(f.Point)).ToList();
                        if (ordered.Count < 2) continue;
                        double span = Coordinate(ordered.Last().Point) - Coordinate(ordered.First().Point);
                        if (span < 50 / 304.8 || span > 1000 / 304.8) continue;
                        var wall = geometry.Hosts.FirstOrDefault(h => h.Key == host.Key)?.Element as Wall;
                        if (wall != null && Math.Abs(span - wall.Width) > 1 / 304.8) continue;
                        string key = Math.Round(Coordinate(ordered.First().Point) * 304.8) + ":" + Math.Round(Coordinate(ordered.Last().Point) * 304.8);
                        if (!seen.Add(key)) continue;
                        XYZ origin = horizontal ? frame.Point(bounds.Right, bounds.Top + frame.Paper(8)) : frame.Point(bounds.Left - frame.Paper(8), bounds.Bottom);
                        AddChain(new[] { ordered.First(), ordered.Last() }.Select(f => new ChainReference { Point = f.Point, Face = f }).ToList(), origin, direction, horizontal, false);
                    }
                }
            }

            void AddChain(List<ChainReference> entries, XYZ origin, XYZ direction, bool horizontal, bool collapse)
            {
                var plan = DetailingChainPlan.Create(entries.Select((p, i) => new DetailingChainPoint(i,
                    (horizontal ? frame.X(p.Point) : frame.Y(p.Point)) * 304.8, p.Run, p.Face != null)), collapse ? 4 : int.MaxValue);
                if (plan.Points.Count < 2) return;
                var anchorFace = entries.Select(p => p.Face).FirstOrDefault(f => f != null);
                var axes = entries.Where(p => p.Axis != null).Select(p => p.Axis!).ToList();
                var references = plan.Points.Select(p =>
                {
                    ChainReference item = entries[p.Id];
                    if (item.Face != null) return item.Face.Reference;
                    var anchor = axes.OrderByDescending(a => Math.Abs((a.Point - item.Point).DotProduct(direction))).FirstOrDefault();
                    return ResolveAxis(doc, view, item.Axis!, anchorFace, origin, direction, options.DimensionTypeId, anchor);
                }).ToList();
                Dimension dimension = CreateDimension(doc, view, origin, direction, references, options.DimensionTypeId);
                doc.Regenerate();
                var segments = dimension.Segments.Cast<DimensionSegment>().ToList();
                foreach (DetailingChainSpan span in plan.EqualSpans)
                {
                    string prefix = span.Intervals + "×" + span.PitchMm.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "=";
                    int index = plan.Points.ToList().FindIndex(p => p.Id == span.Start.Id);
                    if (segments.Count == 0) dimension.Prefix = prefix;
                    else if (index >= 0 && index < segments.Count) segments[index].Prefix = prefix;
                }
                // The continuation pitch belongs to the open crop end, not to a cover.
                if (!frame.Vertical && collapse && horizontal && plan.EqualSpans.Count == 0 && entries[plan.Points[0].Id].Face == null)
                {
                    if (segments.Count > 0) segments[0].Prefix = "шаг ";
                    else dimension.Prefix = "шаг ";
                }
                result.Dimensions++;
            }
        }
    }

    private static bool IsPin(DetailingBar bar) => (bar.Element.LookupParameter("• Деталь • Арматура. Префикс")?.AsString() ?? "") == "Ш"
        || bar.ShapeName.IndexOf("Ш1", StringComparison.OrdinalIgnoreCase) >= 0;
}
