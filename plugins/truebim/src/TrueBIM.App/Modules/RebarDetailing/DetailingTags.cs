using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private sealed class LabelGroup
    {
        public List<DetailingBar> Bars { get; set; } = new();
        public IndependentTag Tag { get; set; } = null!;
        public List<(Reference Reference, XYZ Point)> Targets { get; } = new();
        public DetailingRect Box { get; set; }
        public bool Inline { get; set; }
        public bool CornerCount { get; set; }
        public bool OutsidePlanField { get; set; }
    }

    private static ElementId TagStyle(Document doc, string name, ElementId fallback)
        => new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(s => s.Category?.Id == new ElementId(BuiltInCategory.OST_RebarTags) && s.Name == name)?.Id ?? fallback;

    private static void CompactTags(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry,
        DetailingOptions options, DetailingResult result)
    {
        var markType = TagStyle(doc, "(Без полки) Марка", options.TagTypeId);
        var candidates = new List<(DetailingBar Bar, IndependentTag Tag)>();
        foreach (DetailingBar bar in geometry.Bars)
        {
            IndependentTag tag = IndependentTag.Create(doc, markType, view.Id, bar.Reference, false, TagOrientation.Horizontal, frame.Flatten(bar.Target));
            doc.Regenerate();
            if (!new FilteredElementCollector(doc, view.Id).ToElementIds().Contains(tag.Id))
            {
                foreach (Reference reference in bar.TagReferences)
                {
                    IndependentTag alternative = IndependentTag.Create(doc, markType, view.Id, reference, false, TagOrientation.Horizontal, frame.Flatten(bar.Target));
                    doc.Regenerate();
                    if (new FilteredElementCollector(doc, view.Id).ToElementIds().Contains(alternative.Id))
                    { doc.Delete(tag.Id); tag = alternative; bar.Reference = reference; break; }
                    doc.Delete(alternative.Id);
                }
            }
            candidates.Add((bar, tag));
        }
        var labels = new List<LabelGroup>();
        string MarkKey((DetailingBar Bar, IndependentTag Tag) item) => DetailingGeometry.Key(item.Bar.Host, item.Bar.Link) + "|" + item.Tag.TagText;
        foreach (var group in candidates.GroupBy(p =>
        {
            string comment = p.Bar.Element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
            if (comment.Length > 0 && !Regex.IsMatch(comment, @"^\s*\d+\s*шт\.?\s*$", RegexOptions.IgnoreCase)
                && (comment.StartsWith("ш.", StringComparison.OrdinalIgnoreCase) || comment.StartsWith("шаг", StringComparison.OrdinalIgnoreCase)))
                return MarkKey(p) + "|comment|" + comment;
            double pitch = p.Bar.Spacing;
            if (pitch < 0.1 / 304.8)
            {
                var pitches = candidates.Where(other => MarkKey(other) == MarkKey(p) && other.Bar.Spacing > 0.1 / 304.8)
                    .Select(other => Math.Round(other.Bar.Spacing * 304.8, 1)).Distinct().ToList();
                if (pitches.Count == 1) pitch = pitches[0] / 304.8;
            }
            return MarkKey(p) + "|" + Math.Round(pitch * 304.8, 1);
        }))
        {
            var items = group.OrderByDescending(p => p.Bar.Spacing).ThenBy(p => p.Bar.Element.Id.ToString(), StringComparer.Ordinal).ToList();
            LabelGroup label = new() { Bars = items.Select(p => p.Bar).ToList(), Tag = items[0].Tag };
            foreach (var duplicate in items.Skip(1)) doc.Delete(duplicate.Tag.Id);
            DetailingBar representative = label.Bars[0];
            bool perMetre = label.Bars.Any(b => b.Element.Name.IndexOf("п.м.", StringComparison.OrdinalIgnoreCase) >= 0);
            bool longitudinal = label.Bars.Any(b => b.Segments.Any(s => Math.Abs(s.Direction.DotProduct(frame.Vertical ? frame.Up : frame.Normal)) > 0.999));
            bool shortSystem = representative.Element is RebarInSystem && geometry.Bars.Any(b => b.Host == representative.Host
                && b.Link?.Id == representative.Link?.Id && b.Element.GetTypeId() == representative.Element.GetTypeId()
                && (b.Element.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0)
                    > (representative.Element.get_Parameter(BuiltInParameter.REBAR_ELEM_LENGTH)?.AsDouble() ?? 0) + 200 / 304.8);
            int quantity = label.Bars.Sum(b => b.VisibleIndices.Count);
            bool counted = !perMetre && ((!frame.Vertical && longitudinal && (representative.Element is Rebar || shortSystem || representative.Spacing < 0.1 / 304.8))
                || (frame.Vertical && !longitudinal && quantity <= 3));
            string comments = representative.Element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
            bool textComment = comments.Length > 0 && !Regex.IsMatch(comments, @"^\s*\d+\s*шт\.?\s*$", RegexOptions.IgnoreCase);
            if (counted && options.WriteQuantities)
            {
                foreach (DetailingBar bar in label.Bars)
                {
                    Parameter? parameter = bar.Element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    string previous = parameter?.AsString() ?? "";
                    if (bar.Link != null || parameter == null || parameter.IsReadOnly || bar.Element.Pinned || bar.Element.GroupId != ElementId.InvalidElementId
                        || (previous.Length > 0 && !Regex.IsMatch(previous, @"^\s*\d+\s*шт\.?\s*$", RegexOptions.IgnoreCase)))
                    { result.Notes.Add($"Количество для арматуры {bar.Element.Id}: комментарий или защита элемента сохранены."); continue; }
                    string value = quantity + " шт.";
                    if (previous != value) { parameter.Set(value); result.QuantityWrites++; }
                }
            }
            label.Inline = frame.Vertical && perMetre;
            label.CornerCount = !frame.Vertical && counted && representative.Element is Rebar && representative.ShapeName.EndsWith("000", StringComparison.Ordinal);
            label.OutsidePlanField = !frame.Vertical && !counted && longitudinal && representative.Element is RebarInSystem && !textComment
                && geometry.Bars.GroupBy(b => DetailingGeometry.Key(b.Host, b.Link)).Select(g => LabelHostRegion(frame, geometry, g.ToList()))
                    .Any(r => r.Height > r.Width * 1.5);
            string style = label.Inline ? "(Без полки) Марка" : counted ? "(С полкой) Марка/Комментарии 15"
                : textComment ? Regex.IsMatch(comments, @"^ш\.?\s*\d", RegexOptions.IgnoreCase)
                    ? "(С полкой) Марка/Комментарии 15" : "(С полкой) Марка/Комментарии"
                : frame.Vertical ? "(С полкой) Марка/Шаг 15" : "(С полкой) Марка/Шаг 10";
            // Choose one visible reference per face. Small tie sets have a leader to
            // each bar; the quantity is the combined visible count, not one set's count.
            foreach (DetailingBar bar in label.Bars.OrderBy(b => frame.X(b.Target)).ThenBy(b => frame.Y(b.Target)))
            {
                if (!counted && !textComment && Math.Abs(bar.Spacing - representative.Spacing) > 0.1 / 304.8) continue;
                if ((counted || textComment) && (bar.Element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "")
                    != (representative.Element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "")) continue;
                if (label.Targets.Count >= (IsPin(bar) ? 1 : 2)) break;
                var positions = bar.Positions.Select((point, index) => new { Point = point, Index = index }).ToList();
                if (counted && bar.Positions.Count <= 3 && label.Bars.Count == 1)
                {
                    for (int i = 0; i < positions.Count; i++) label.Targets.Add((bar.PositionReferences[i], positions[i].Point));
                }
                else label.Targets.Add((bar.Reference, bar.Target));
            }
            if (label.Targets.Count == 0) label.Targets.Add((representative.Reference, representative.Target));
            if (label.CornerCount || label.OutsidePlanField)
            {
                var lowest = label.Bars.SelectMany(b => b.Positions.Select((point, index) => (Reference: b.PositionReferences[index], Point: point)))
                    .OrderBy(p => Math.Round(frame.Y(p.Point) * 304.8, 1)).ThenBy(p => frame.X(p.Point)).First();
                label.Targets.Clear();
                label.Targets.Add(lowest);
            }
            // The initial tag reference remains a leader in Revit even if only new
            // references receive explicit end points. Start with the chosen reference.
            doc.Delete(label.Tag.Id);
            label.Tag = IndependentTag.Create(doc, TagStyle(doc, style, options.TagTypeId), view.Id, label.Targets[0].Reference,
                false, label.Inline ? TagOrientation.Vertical : TagOrientation.Horizontal, frame.Flatten(label.Targets[0].Point));
            labels.Add(label);
        }
        doc.Regenerate();
        double unit = frame.Paper(1);
        DetailingRect Paper(DetailingRect box) => new(box.Left / unit, box.Bottom / unit, box.Right / unit, box.Top / unit);
        var regionsByHost = geometry.Bars.GroupBy(b => DetailingGeometry.Key(b.Host, b.Link))
            .ToDictionary(g => g.Key, g => LabelHostRegion(frame, geometry, g.ToList()));
        var hostRegions = regionsByHost.Values.ToList();
        double nodeCenter = (hostRegions.Min(r => r.Left) + hostRegions.Max(r => r.Right)) / 2;
        var obstacles = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType()
            .Where(e => e is TextNote || e is AnnotationSymbol)
            .Select(e => LabelObstacle(e, view, frame)).Where(b => b.HasValue).Select(b => Paper(b!.Value)).ToList();
        obstacles.AddRange(DimensionTextObstacles(doc, view, frame).Select(Paper));
        var textObstacles = obstacles.ToList();
        bool hasTicks = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
            .Any(t => t.StyleType == DimensionStyleType.Linear && t.Name == "• Засечки");
        if (!frame.Vertical)
            obstacles.AddRange(hostRegions.Select(Paper));
        var requests = new List<DetailingLabelRequest>();
        for (int i = 0; i < labels.Count; i++)
        {
            LabelGroup label = labels[i];
            var hostKey = DetailingGeometry.Key(label.Bars[0].Host, label.Bars[0].Link);
            DetailingRect host = regionsByHost[hostKey];
            if (label.Inline)
            {
                var field = Region(frame, label.Bars);
                label.Tag.TagHeadPosition = frame.Point(host.Left - frame.Paper(options.Details ? 11 : 6), field.CenterY);
                result.ExpectedTagHeads[label.Tag.Id] = label.Tag.TagHeadPosition;
                doc.Regenerate();
                result.Tags++; continue;
            }
            // A native tag box can include its host even with the leader disabled.
            // These project families encode their paper shelf width in the type.
            Element labelType = doc.GetElement(label.Tag.GetTypeId());
            string typeName = labelType.Name;
            var shelf = Regex.Match(typeName, @" (\d+)$");
            double paperWidth = shelf.Success ? double.Parse(shelf.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 20;
            double nativeWidth = (labelType.LookupParameter("Ширина полки")?.AsDouble() ?? 0) * 304.8;
            if (nativeWidth >= 1 && nativeWidth <= 100) paperWidth = nativeWidth;
            double width = frame.Paper(paperWidth + 2), height = frame.Paper(7);
            double left = -width / 2, bottom = -height / 2;
            label.Box = new DetailingRect(left, bottom, left + width, bottom + height);
            var target = label.Targets[label.Targets.Count / 2].Point;
            if (!frame.Vertical && label.Bars.Any(b => b.Element.LookupParameter("• Деталь • Арматура. Префикс")?.AsString() == "П"))
            {
                var horizontalHost = hostRegions.Where(r => r.Width > r.Height * 1.5).OrderByDescending(r => r.Width).ToList();
                if (horizontalHost.Count > 0) host = horizontalHost[0];
            }
            DetailingLabelSide side = frame.Vertical ? DetailingLabelSide.Right : host.Width > host.Height ? DetailingLabelSide.Above
                : host.CenterX < nodeCenter ? DetailingLabelSide.Right : DetailingLabelSide.Left;
            var request = new DetailingLabelRequest { Id = i, X = frame.X(target) / unit, Y = frame.Y(target) / unit,
                Width = width / unit, Height = height / unit, Host = Paper(host), PreferredSide = side, OnlyPreferredSide = true };
            request.Targets.AddRange(label.Targets.Select(t => (frame.X(t.Point) / unit, frame.Y(t.Point) / unit)));
            if (hasTicks && !label.CornerCount && !label.OutsidePlanField && label.Bars.Count >= 2)
            {
                XYZ along = frame.Vertical ? frame.Up : frame.Right;
                XYZ across = frame.Vertical ? frame.Right : frame.Up;
                var axes = label.Bars.SelectMany(b => b.DimensionReferences).Where(a => Math.Abs(a.Direction.DotProduct(along)) > 0.999)
                    .OrderBy(a => a.Point.DotProduct(across)).ToList();
                if (axes.Count >= 2 && Math.Abs((axes.Last().Point - axes.First().Point).DotProduct(across)) > 1 / 304.8)
                {
                    DetailingRect field = Region(frame, label.Bars);
                    request.SlidingTarget = true; request.SlideAlongX = !frame.Vertical;
                    request.SlideMin = (frame.Vertical ? field.Bottom : field.Left) / unit;
                    request.SlideMax = (frame.Vertical ? field.Top : field.Right) / unit;
                    request.SlideFixed = (frame.Vertical ? frame.X(axes.Last().Point) : frame.Y(axes.Last().Point)) / unit;
                }
            }
            if (label.CornerCount)
            {
                DetailingRect corner = Region(frame, label.Bars);
                request.Host = new DetailingRect(hostRegions.Min(r => r.Left) / unit - 12, corner.Bottom / unit,
                    hostRegions.Max(r => r.Right) / unit + 12, corner.Top / unit);
                request.PreferredSide = corner.CenterX < nodeCenter ? DetailingLabelSide.Left : DetailingLabelSide.Right;
                request.PreferredY = corner.Bottom / unit - 6;
            }
            else if (label.OutsidePlanField && host.Width > host.Height)
            {
                bool rightCorner = hostRegions.Any(r => r.Height > r.Width * 1.5 && r.CenterX > host.CenterX);
                request.PreferredSide = rightCorner ? DetailingLabelSide.Left : DetailingLabelSide.Right;
                request.Host = new DetailingRect(host.Left / unit - 8, host.Bottom / unit, host.Right / unit + 8, host.Top / unit);
                request.PreferredY = host.Bottom / unit - 6;
            }
            if (!frame.Vertical && !label.CornerCount && !label.OutsidePlanField)
            {
                var horizontal = hostRegions.Where(r => r.Width > r.Height * 1.5).OrderByDescending(r => r.Width).FirstOrDefault();
                var legs = hostRegions
                    .Where(r => r.Height > r.Width * 1.5 && Math.Abs(r.Right - horizontal.Right) < frame.Paper(8) && r.Top > horizontal.Top + frame.Paper(8)).ToList();
                if (legs.Count > 0)
                {
                    request.MaxX = legs.Min(r => r.Left) / unit - 2;
                    request.MinX = Math.Min(horizontal.Left / unit - 15, request.MaxX - request.Width);
                    request.MinY = horizontal.Top / unit + 3;
                    if (side == DetailingLabelSide.Left) request.Host = new DetailingRect(request.Host.Left + 3, request.Host.Bottom, request.Host.Right, request.Host.Top);
                    if (labels.Count > 7 && request.Width > 20)
                    {
                        request.PreferredSide = DetailingLabelSide.Right;
                        request.Host = Paper(legs.OrderByDescending(r => r.Height).First());
                        request.MinX = double.NegativeInfinity; request.MaxX = double.PositiveInfinity;
                        request.PreferredY = horizontal.Top / unit + 12;
                    }
                }
                var leftLegs = hostRegions.Where(r => r.Height > r.Width * 1.5 && Math.Abs(r.Left - horizontal.Left) < frame.Paper(8)
                    && r.Top > horizontal.Top + frame.Paper(8)).ToList();
                if (leftLegs.Count > 0)
                {
                    request.MinX = leftLegs.Max(r => r.Right) / unit + 2;
                    request.MaxX = Math.Max(horizontal.Right / unit + 1, request.MinX + request.Width);
                    request.MinY = horizontal.Top / unit + 3;
                    if (side == DetailingLabelSide.Right) request.Host = new DetailingRect(request.Host.Left, request.Host.Bottom, request.Host.Right - 3, request.Host.Top);
                    if (labels.Count > 7 && request.Width > 20)
                    {
                        request.PreferredSide = DetailingLabelSide.Left;
                        request.Host = Paper(leftLegs.OrderByDescending(r => r.Height).First());
                        request.MinX = double.NegativeInfinity; request.MaxX = double.PositiveInfinity;
                        request.PreferredY = horizontal.Top / unit + 12;
                    }
                }
            }
            requests.Add(request);
        }
        foreach (var placement in DetailingLabelLayout.Place(requests, obstacles, 5, 2, textObstacles))
        {
            LabelGroup label = labels[placement.Request.Id];
            IndependentTag tag = label.Tag;
            result.LayoutTrace.Add(System.FormattableString.Invariant($"{tag.TagText} | host {label.Bars[0].Host} | corner {label.CornerCount} | {placement.Side} | anchor {placement.Request.X:0.0},{placement.Request.Y:0.0} | hostBox {placement.Request.Host.Left:0.0},{placement.Request.Host.Bottom:0.0},{placement.Request.Host.Right:0.0},{placement.Request.Host.Top:0.0} | headBox {placement.Bounds.Left:0.0},{placement.Bounds.Bottom:0.0},{placement.Bounds.Right:0.0},{placement.Bounds.Top:0.0} | nodeCenter {nodeCenter / unit:0.0}"));
            XYZ head = frame.Point(placement.Bounds.Left * unit - label.Box.Left, placement.Bounds.Bottom * unit - label.Box.Bottom);
            tag.TagHeadPosition = head;
            doc.Regenerate();
            if (!label.CornerCount && !label.OutsidePlanField && TryTagTick(doc, view, frame, label, head, out XYZ tickEnd))
            {
                label.Targets.Clear(); label.Targets.Add((label.Bars[0].Reference, tickEnd)); result.Dimensions++;
                ElementId tagType = tag.GetTypeId();
                doc.Delete(tag.Id);
                tag = IndependentTag.Create(doc, tagType, view.Id, label.Targets[0].Reference, false, TagOrientation.Horizontal, head);
                label.Tag = tag;
            }
            result.ExpectedTagHeads[tag.Id] = head;
            tag.HasLeader = true;
            tag.LeaderEndCondition = LeaderEndCondition.Free;
#if REVIT2022_OR_GREATER
            var existing = tag.GetTaggedReferences().Select(r => r.ConvertToStableRepresentation(doc)).ToHashSet();
            var additional = label.Targets.Select(t => t.Reference).Where(r => !existing.Contains(r.ConvertToStableRepresentation(doc)))
                .GroupBy(r => r.ConvertToStableRepresentation(doc)).Select(g => g.First()).ToList();
            if (additional.Count > 0) tag.AddReferences(additional);
            foreach (var target in label.Targets)
            {
                tag.SetLeaderEnd(target.Reference, frame.Flatten(target.Point));
                tag.SetLeaderElbow(target.Reference, frame.Point(placement.EndX * unit, frame.Y(head)));
            }
#else
            tag.LeaderEnd = frame.Flatten(label.Targets[0].Point);
            tag.LeaderElbow = frame.Point(placement.EndX * unit, frame.Y(head));
#endif
            tag.TagHeadPosition = head;
            doc.Regenerate();
            if (placement.Crowded) result.Notes.Add("Плотное оформление: проверьте положение марки «" + tag.TagText + "».");
            result.Tags++;
        }
    }

    private static IEnumerable<DetailingRect> DimensionTextObstacles(Document doc, View view, DetailingFrame frame)
    {
        foreach (Dimension dimension in new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension)))
        {
            if (dimension is SpotDimension || dimension.Curve is not Line line) continue;
            double height = dimension.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() * view.Scale ?? frame.Paper(2.5);
            bool horizontal = Math.Abs(line.Direction.DotProduct(frame.Right)) > 0.7;
            var texts = new List<(XYZ Point, string Text)>();
            if (dimension.NumberOfSegments > 1)
            {
                foreach (DimensionSegment segment in dimension.Segments)
                    if (segment.IsTextPositionAdjustable()) texts.Add((segment.TextPosition, segment.Prefix + segment.ValueString + segment.Suffix));
            }
            else if (dimension.IsTextPositionAdjustable()) texts.Add((dimension.TextPosition, dimension.Prefix + dimension.ValueString + dimension.Suffix));
            foreach (var text in texts)
            {
                double width = Math.Max(frame.Paper(2), text.Text.Length * height * 0.65);
                double halfX = (horizontal ? width : height) / 2 + frame.Paper(0.6);
                double halfY = (horizontal ? height : width) / 2 + frame.Paper(0.6);
                double x = frame.X(text.Point), y = frame.Y(text.Point);
                yield return new DetailingRect(x - halfX, y - halfY, x + halfX, y + halfY);
            }
        }
    }

    private static DetailingRect? LabelObstacle(Element element, View view, DetailingFrame frame)
    {
        if (element is AnnotationSymbol annotation && annotation.Symbol.FamilyName == "(Аннотация) Выноска")
        {
            XYZ head = annotation.GetTransform().Origin;
            double width = (annotation.LookupParameter("Длина полки")?.AsDouble() ?? 20 / 304.8) * view.Scale;
            double x = frame.X(head), y = frame.Y(head);
            return new DetailingRect(x - width / 2 - frame.Paper(1), y - frame.Paper(3.5), x + width / 2 + frame.Paper(1), y + frame.Paper(3.5));
        }
        var bounds = Bounds(element, view, frame);
        return bounds.HasValue ? new DetailingRect(bounds.Value.MinX, bounds.Value.MinY, bounds.Value.MaxX, bounds.Value.MaxY) : null;
    }

    private static DetailingRect LabelHostRegion(DetailingFrame frame, DetailingGeometry geometry, List<DetailingBar> bars)
    {
        DetailingRect region = Region(frame, bars);
        if (frame.Vertical) return region;
        string key = DetailingGeometry.Key(bars[0].Host, bars[0].Link);
        if (geometry.Hosts.FirstOrDefault(host => host.Key == key)?.Element is not Wall wall
            || wall.Location is not LocationCurve location) return region;
        XYZ direction = location.Curve.GetEndPoint(1) - location.Curve.GetEndPoint(0);
        if (bars[0].Link != null) direction = bars[0].Link!.GetTotalTransform().OfVector(direction);
        bool horizontal = Math.Abs(direction.DotProduct(frame.Right)) > Math.Abs(direction.DotProduct(frame.Up));
        var faces = geometry.Faces.Where(face => face.HostKey == key && face.Horizontal != horizontal).ToList();
        if (faces.Count < 2) return region;
        // Bent bars assigned to this wall can reach into its neighbour. Use the
        // concrete sides for wall thickness, retaining the visible run length.
        return horizontal ? new DetailingRect(region.Left, faces.Min(face => frame.Y(face.Point)), region.Right, faces.Max(face => frame.Y(face.Point)))
            : new DetailingRect(faces.Min(face => frame.X(face.Point)), region.Bottom, faces.Max(face => frame.X(face.Point)), region.Top);
    }

    private static bool TryTagTick(Document doc, View view, DetailingFrame frame, LabelGroup label, XYZ head, out XYZ end)
    {
        end = head;
        if (label.Bars.Count < 2) return false;
        var style = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
            .FirstOrDefault(t => t.StyleType == DimensionStyleType.Linear && t.Name == "• Засечки");
        if (style == null) return false;
        XYZ along = frame.Vertical ? frame.Up : frame.Right;
        XYZ across = frame.Vertical ? frame.Right : frame.Up;
        var axes = label.Bars.SelectMany(b => b.DimensionReferences).Where(a => Math.Abs(a.Direction.DotProduct(along)) > 0.999)
            .OrderBy(a => a.Point.DotProduct(across)).ToList();
        if (axes.Count < 2 || Math.Abs((axes.Last().Point - axes.First().Point).DotProduct(across)) < 1 / 304.8) return false;
        DetailingRect bounds = Region(frame, label.Bars);
        double position = frame.Vertical ? Math.Max(bounds.Bottom, Math.Min(bounds.Top, frame.Y(head)))
            : Math.Max(bounds.Left, Math.Min(bounds.Right, frame.X(head)));
        XYZ origin = frame.Vertical ? frame.Point(bounds.Left, position) : frame.Point(position, bounds.Bottom);
        doc.Regenerate();
        using SubTransaction attempt = new(doc); attempt.Start();
        try
        {
            Reference first = ResolveAxis(doc, view, axes.First(), null, origin, across, style.Id, axes.Last());
            Reference last = ResolveAxis(doc, view, axes.Last(), null, origin, across, style.Id, axes.First());
            CreateDimension(doc, view, origin, across, new[] { first, last }, style.Id);
            end = frame.Vertical ? frame.Point(frame.X(axes.Last().Point), position) : frame.Point(position, frame.Y(axes.Last().Point));
            attempt.Commit(); return true;
        }
        catch { if (attempt.GetStatus() == TransactionStatus.Started) attempt.RollBack(); return false; }
    }
}
