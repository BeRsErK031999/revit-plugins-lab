using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private static readonly Guid DetailSchemaId = new("BFA6314C-B0ED-4A57-AB73-E4956BAC54DB");
    private sealed class DetailingCut
    {
        public string Host { get; set; } = "";
        public bool HorizontalRun { get; set; }
        public bool Low { get; set; }
        public double Coordinate { get; set; }
        public XYZ Start { get; set; } = null!;
        public XYZ End { get; set; } = null!;
        public bool Includes(DetailingFrame frame, XYZ point) => Low
            ? (HorizontalRun ? frame.X(point) : frame.Y(point)) >= Coordinate - 0.1 / 304.8
            : (HorizontalRun ? frame.X(point) : frame.Y(point)) <= Coordinate + 0.1 / 304.8;
    }

    private static List<DetailingCut> PrepareCuts(DetailingFrame frame, DetailingGeometry geometry)
    {
        var cuts = new List<DetailingCut>();
        if (frame.Vertical) return cuts;
        if (!new FilteredElementCollector(frame.View.Document).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .Any(s => s.FamilyName == "(Узел) Область обрыва • 1" && s.Name == "M" + frame.View.Scale)) return cuts;
        foreach (var host in geometry.Bars.GroupBy(b => DetailingGeometry.Key(b.Host, b.Link)))
        {
            DetailingRect bounds = Region(frame, host);
            bool horizontal = bounds.Width > bounds.Height;
            double low = horizontal ? bounds.Left : bounds.Bottom, high = horizontal ? bounds.Right : bounds.Top;
            if (high - low < frame.Paper(20)) continue;
            var faces = geometry.Faces.Where(f => f.Horizontal == horizontal
                && (horizontal ? f.Bounds.Top >= bounds.Bottom && f.Bounds.Bottom <= bounds.Top
                    : f.Bounds.Right >= bounds.Left && f.Bounds.Left <= bounds.Right)).ToList();
            foreach (bool atLow in new[] { true, false })
            {
                double boundary = atLow ? low : high;
                if (faces.Any(f => Math.Abs((horizontal ? frame.X(f.Point) : frame.Y(f.Point)) - boundary) < frame.Paper(4))) continue;
                // A detail break sits inside the crop so its masking region can hide
                // the unfinished end. Its hidden bars must not contribute to counts.
                double position = boundary + (atLow ? 1 : -1) * frame.Paper(5);
                var manual = new FilteredElementCollector(frame.View.Document).OwnedByView(frame.View.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(f => !OwnedDetail(f) && f.Symbol.FamilyName == "(Узел) Область обрыва • 1" && f.Location is LocationCurve)
                    .Select(f => ((LocationCurve)f.Location).Curve).OfType<Line>()
                    .Where(line => Math.Abs(line.Direction.DotProduct(horizontal ? frame.Up : frame.Right)) > 0.999)
                    .Select(line => line.Evaluate(0.5, true)).Where(point => Math.Abs((horizontal ? frame.X(point) : frame.Y(point)) - boundary) < frame.Paper(12)
                        && (horizontal ? frame.Y(point) >= bounds.Bottom - frame.Paper(2) && frame.Y(point) <= bounds.Top + frame.Paper(2)
                            : frame.X(point) >= bounds.Left - frame.Paper(2) && frame.X(point) <= bounds.Right + frame.Paper(2)))
                    .OrderBy(point => Math.Abs((horizontal ? frame.X(point) : frame.Y(point)) - boundary)).FirstOrDefault();
                if (manual != null) position = horizontal ? frame.X(manual) : frame.Y(manual);
                var crossFaces = geometry.Faces.Where(f => f.HostKey == host.Key && f.Horizontal != horizontal).ToList();
                if (crossFaces.Count < 2) continue;
                double crossLow = crossFaces.Min(f => horizontal ? frame.Y(f.Point) : frame.X(f.Point));
                double crossHigh = crossFaces.Max(f => horizontal ? frame.Y(f.Point) : frame.X(f.Point));
                if (crossHigh - crossLow > 1000 / 304.8) continue;
                cuts.Add(new DetailingCut { Host = host.Key, HorizontalRun = horizontal, Low = atLow, Coordinate = position,
                    Start = horizontal ? frame.Point(position, crossLow) : frame.Point(crossLow, position),
                    End = horizontal ? frame.Point(position, crossHigh) : frame.Point(crossHigh, position) });
            }
        }
        foreach (var cut in cuts)
        foreach (var bar in geometry.Bars.Where(b => DetailingGeometry.Key(b.Host, b.Link) == cut.Host).ToList())
        {
            var indices = bar.VisibleIndices.OrderBy(i => i).ToList();
            for (int index = bar.Positions.Count - 1; index >= 0; index--)
            {
                if (cut.Includes(frame, bar.Positions[index])) continue;
                bar.Positions.RemoveAt(index); bar.PositionReferences.RemoveAt(index); bar.VisibleIndices.Remove(indices[index]);
            }
            bar.DimensionReferences.RemoveAll(axis => !cut.Includes(frame, axis.Point));
            for (int index = bar.Segments.Count - 1; index >= 0; index--)
            {
                var segment = bar.Segments[index];
                bool start = cut.Includes(frame, segment.Start), end = cut.Includes(frame, segment.End);
                if (!start && !end) { bar.Segments.RemoveAt(index); continue; }
                if (start && end) continue;
                double a = cut.HorizontalRun ? frame.X(segment.Start) : frame.Y(segment.Start);
                double b = cut.HorizontalRun ? frame.X(segment.End) : frame.Y(segment.End);
                XYZ intersection = segment.Start + (segment.End - segment.Start) * ((cut.Coordinate - a) / (b - a));
                if (!start) segment.Start = intersection; else segment.End = intersection;
            }
            if (bar.Positions.Count == 0) geometry.Bars.Remove(bar);
            else bar.Reference = bar.PositionReferences[bar.PositionReferences.Count / 2];
        }
        return cuts;
    }

    internal static bool OwnedDetail(Element element)
    {
        Schema? schema = Schema.Lookup(DetailSchemaId);
        return schema != null && element.GetEntity(schema).IsValid();
    }

    private static void MarkDetail(Element element, string role)
    {
        Schema? schema = Schema.Lookup(DetailSchemaId);
        if (schema == null)
        {
            SchemaBuilder builder = new(DetailSchemaId);
            builder.SetSchemaName("TrueBIMSectionDetail");
            builder.AddSimpleField("Role", typeof(string));
            schema = builder.Finish();
        }
        Entity data = new(schema); data.Set(schema.GetField("Role"), role); element.SetEntity(data);
    }

    private static void Details(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry,
        IReadOnlyList<DetailingCut> cuts, DetailingResult result)
    {
        var preserved = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().Where(e => !OwnedDetail(e)).ToList();
        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
        FamilySymbol? breakType = symbols.FirstOrDefault(s => s.FamilyName == "(Узел) Область обрыва • 1" && s.Name == "M" + view.Scale);
        if (breakType != null && !breakType.IsActive) breakType.Activate();
        foreach (var cut in cuts) AddBreak(cut.Start, cut.End, cut.Low);
        if (!frame.Vertical) return;
        foreach (var host in geometry.Bars.GroupBy(b => DetailingGeometry.Key(b.Host, b.Link)))
        {
            var sideFaces = geometry.Faces.Where(f => f.HostKey == host.Key && f.Horizontal).OrderBy(f => frame.X(f.Point)).ToList();
            var levels = geometry.Faces.Where(f => f.HostKey == host.Key && !f.Horizontal).OrderBy(f => frame.Y(f.Point)).ToList();
            if (sideFaces.Count < 2 || levels.Count < 2) continue;
            double left = frame.X(sideFaces.First().Point), right = frame.X(sideFaces.Last().Point);
            if (right - left > 1000 / 304.8) continue;
            double bottom = frame.Y(levels.First().Point), top = frame.Y(levels.Last().Point);
            DetailingRect barBounds = Region(frame, host);
            foreach (double y in new[] { bottom, top }) AddJoint(frame.Point(left, y), frame.Point(right, y));
            double upperBreak = barBounds.Top + frame.Paper(2);
            AddBreak(frame.Point(left, upperBreak), frame.Point(right, upperBreak), true);
            double lowerBreak = bottom - frame.Paper(6);
            if (!AddFoundationBreak(left, right, lowerBreak))
                AddBreak(frame.Point(left - frame.Paper(5), lowerBreak), frame.Point(right + frame.Paper(5), lowerBreak), false);
            var slab = geometry.Faces.Where(f => !f.Horizontal && f.HostKey != host.Key && frame.Y(f.Point) > top + 1 / 304.8
                && frame.Y(f.Point) < top + 1000 / 304.8).OrderBy(f => frame.Y(f.Point)).FirstOrDefault();
            if (slab != null)
            {
                double slabTop = frame.Y(slab.Point);
                AddBreak(frame.Point(left - frame.Paper(5), top), frame.Point(left - frame.Paper(5), slabTop), true);
                AddNote("Плита", "перекрытия", frame.Point(right + frame.Paper(15), slabTop + frame.Paper(5)), frame.Point((left + right) / 2, (top + slabTop) / 2), 20);
            }
            string foundation = geometry.Hosts.Where(h => h.Link != null && (h.Element is Floor || h.Element.Category?.Id == new ElementId(BuiltInCategory.OST_StructuralFoundation))
                && h.Bounds.Bottom <= bottom && h.Bounds.Top >= bottom - frame.Paper(6))
                .OrderBy(h => h.Link!.Name.Contains("_ALL") ? 1 : 0).FirstOrDefault()?.Link?.Name ?? "";
            var mark = System.Text.RegularExpressions.Regex.Match(foundation, @"КЖ\d+(?:\.\d+)*");
            AddNote("Фундаментная плита", mark.Success ? mark.Value : " ", frame.Point(right + frame.Paper(30), bottom - frame.Paper(12)), frame.Point((left + right) / 2, bottom - frame.Paper(2)), 32);
        }

        bool AddFoundationBreak(double left, double right, double y)
        {
            if (preserved.OfType<FamilyInstance>().Any(f => f.Symbol.FamilyName == "(Узел) Область обрыва • 3")) return true;
            FamilyInstance? template = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                .Where(f => f.Symbol.FamilyName == "(Узел) Область обрыва • 3" && f.Name == "M" + view.Scale && f.OwnerViewId != view.Id
                    && doc.GetElement(f.OwnerViewId) is View source && source.ViewDirection.DotProduct(frame.Normal) > 0.999)
                .OrderBy(f => f.Location is LocationPoint point ? point.Point.DistanceTo(frame.Point(right, y)) : double.MaxValue).FirstOrDefault();
            if (template == null) return false;
            var copied = ElementTransformUtils.CopyElements((View)doc.GetElement(template.OwnerViewId), new[] { template.Id }, view, Transform.Identity, new CopyPasteOptions());
            foreach (var id in copied)
            {
                Element element = doc.GetElement(id);
                if (element.Location is LocationPoint location) location.Point = frame.Point(right + frame.Paper(5), y);
                element.LookupParameter("Длина линии 1")?.Set(frame.Paper(6));
                element.LookupParameter("Длина линии 2")?.Set(right - left + frame.Paper(10));
                element.LookupParameter("Длина линии 3")?.Set(frame.Paper(6));
                MarkDetail(element, "break"); result.Details++;
            }
            return true;
        }

        void AddBreak(XYZ start, XYZ end, bool reverse)
        {
            if (breakType == null) { result.Notes.Add("Для обрывов загрузите семейство «(Узел) Область обрыва • 1» с типом M" + view.Scale + "."); return; }
            XYZ middle = (start + end) / 2;
            if (preserved.OfType<FamilyInstance>().Any(f => f.Symbol.FamilyName.StartsWith("(Узел) Область обрыва")
                && (f.Location is LocationPoint point ? frame.Flatten(point.Point).DistanceTo(middle) < frame.Paper(10)
                    : f.Location is LocationCurve curve && frame.Flatten(curve.Curve.Evaluate(0.5, true)).DistanceTo(middle) < frame.Paper(10)))) return;
            if (view is ViewPlan plan)
            {
                double elevation = plan.GenLevel.ProjectElevation;
                start = new XYZ(start.X, start.Y, elevation); end = new XYZ(end.X, end.Y, elevation);
            }
            Line line = Line.CreateBound(reverse ? end : start, reverse ? start : end);
            FamilyInstance detail = doc.Create.NewFamilyInstance(line, breakType, view);
            foreach (Parameter length in detail.Parameters)
                if (length.Definition.Name == "Длина" && length.StorageType == StorageType.Double && !length.IsReadOnly)
                    length.Set(start.DistanceTo(end));
            MarkDetail(detail, "break"); result.Details++;
        }
        void AddJoint(XYZ start, XYZ end)
        {
            if (preserved.OfType<CurveElement>().Any(c => c.Name.Contains("Бетон") && frame.Flatten(c.GeometryCurve.Evaluate(0.5, true)).DistanceTo((start + end) / 2) < frame.Paper(2))) return;
            string name = "Бетон • Шов • M" + view.Scale;
            CurveElement? template = new FilteredElementCollector(doc).OfClass(typeof(CurveElement)).Cast<CurveElement>()
                .FirstOrDefault(c => c.Name == name && c.OwnerViewId != view.Id);
            if (template != null)
            {
                var copied = ElementTransformUtils.CopyElements((View)doc.GetElement(template.OwnerViewId), new[] { template.Id }, view, Transform.Identity, new CopyPasteOptions());
                foreach (var id in copied)
                {
                    Element element = doc.GetElement(id);
                    if (element is CurveElement curve) curve.GeometryCurve = Line.CreateBound(start, end);
                    MarkDetail(element, "joint"); result.Details++;
                }
            }
            else
            {
                int count = Math.Max(6, (int)Math.Ceiling(start.DistanceTo(end) / frame.Paper(0.8)));
                XYZ last = start;
                for (int i = 1; i <= count; i++)
                {
                    XYZ next = start + (end - start) * i / count + (i == count ? XYZ.Zero : frame.Up * frame.Paper(i % 2 == 0 ? -0.35 : 0.35));
                    var line = doc.Create.NewDetailCurve(view, Line.CreateBound(last, next)); MarkDetail(line, "joint"); result.Details++; last = next;
                }
            }
        }
        void AddNote(string upper, string lower, XYZ head, XYZ target, double width)
        {
            if (preserved.OfType<FamilyInstance>().Any(f => (f.LookupParameter("Верхняя строка")?.AsString() ?? "").TrimStart().StartsWith(upper, StringComparison.OrdinalIgnoreCase))
                || preserved.OfType<TextNote>().Any(note => note.Text.TrimStart().StartsWith(upper, StringComparison.OrdinalIgnoreCase))) return;
            FamilySymbol? symbol = symbols.FirstOrDefault(s => s.FamilyName == "(Аннотация) Выноска" && s.Name == "• Без указателя");
            if (symbol == null) { result.Notes.Add("Для подписей плит загрузите семейство «(Аннотация) Выноска»."); return; }
            if (!symbol.IsActive) symbol.Activate();
            FamilyInstance note = doc.Create.NewFamilyInstance(head, symbol, view);
            note.LookupParameter("Верхняя строка")?.Set(upper); note.LookupParameter("Нижняя строка")?.Set(lower);
            note.LookupParameter("Длина полки")?.Set(width / 304.8);
            if (note is AnnotationSymbol annotation)
            {
                annotation.addLeader(); doc.Regenerate();
                var leader = annotation.GetLeaders().Cast<Leader>().Last(); leader.End = target; leader.Elbow = head - frame.Right * frame.Paper(width / 2);
            }
            MarkDetail(note, "note"); result.Details++;
        }
    }
}
