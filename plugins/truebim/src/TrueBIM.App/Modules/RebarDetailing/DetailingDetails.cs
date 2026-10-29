using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    private static readonly Guid DetailSchemaId = new("BFA6314C-B0ED-4A57-AB73-E4956BAC54DB");

    internal static bool OwnedDetail(Element element)
    {
        Schema? schema = Schema.Lookup(DetailSchemaId);
        return schema != null && element.GetEntity(schema).IsValid();
    }

    private static string DetailRole(Element element)
    {
        Schema? schema = Schema.Lookup(DetailSchemaId);
        if (schema == null) return "";
        Entity entity = element.GetEntity(schema);
        return entity.IsValid() ? entity.Get<string>(schema.GetField("Role")) : "";
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

    private static DetailingRect SectionAnnotationBoundary(Document doc, View view, DetailingFrame frame, DetailingSection section)
    {
        double left = section.BreakLeft, right = section.BreakRight, bottom = section.BreakBottom;
        // Dimension offsets start at the actual break line, not its masking box.
        // A masking region may extend hundreds of millimetres beyond that line.
        foreach (var detail in new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
            .Where(f => f.Symbol.FamilyName.StartsWith("(Узел) Область обрыва", StringComparison.Ordinal)))
        {
            if (detail.Location is LocationPoint point)
            {
                double length = detail.LookupParameter("Длина линии 2")?.AsDouble() ?? 0;
                XYZ along = detail.GetTransform().BasisY;
                double level = frame.Y(point.Point);
                if (length > 0 && Math.Abs(along.DotProduct(frame.Right)) > 0.999 && level < section.Wall.Bottom
                    && level >= section.Wall.Bottom - frame.Paper(20))
                {
                    double aX = frame.X(point.Point), bX = frame.X(point.Point + along * length);
                    if (Math.Max(aX, bX) >= section.Wall.Left && Math.Min(aX, bX) <= section.Wall.Right)
                    { left = Math.Min(left, Math.Min(aX, bX)); right = Math.Max(right, Math.Max(aX, bX)); bottom = Math.Min(bottom, level); }
                }
                continue;
            }
            if (detail.Location is not LocationCurve location || location.Curve is not Line line) continue;
            XYZ a = line.GetEndPoint(0), b = line.GetEndPoint(1);
            double x = frame.X((a + b) / 2), y = frame.Y((a + b) / 2);
            if (Math.Abs(line.Direction.DotProduct(frame.Up)) > 0.999
                && x >= section.Wall.Left - frame.Paper(30) && x <= section.Wall.Right + frame.Paper(30)
                && Math.Max(frame.Y(a), frame.Y(b)) >= section.Wall.Bottom && Math.Min(frame.Y(a), frame.Y(b)) <= section.Reinforcement.Top)
            { left = Math.Min(left, x); right = Math.Max(right, x); }
            if (Math.Abs(line.Direction.DotProduct(frame.Right)) > 0.999
                && y < section.Wall.Bottom && y >= section.Wall.Bottom - frame.Paper(20)
                && Math.Max(frame.X(a), frame.X(b)) >= section.Wall.Left && Math.Min(frame.X(a), frame.X(b)) <= section.Wall.Right)
                bottom = Math.Min(bottom, y);
        }
        return new DetailingRect(left, bottom, right, section.Reinforcement.Top);
    }

    private static void Details(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry,
        DetailingOptions options, DetailingResult result)
    {
        var preserved = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().Where(e => !OwnedDetail(e)).ToList();
        var symbols = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().ToList();
        var createdBreaks = new HashSet<string>();
        foreach (var section in Sections(frame, geometry, options))
        {
            double left = section.Wall.Left, right = section.Wall.Right, bottom = section.Wall.Bottom, top = section.Wall.Top;
            if (section.Faces.Count(f => f.Horizontal) < 2 || section.Faces.Count(f => !f.Horizontal) < 2) continue;
            foreach (double y in new[] { bottom, top }) AddJoint(frame.Point(left, y), frame.Point(right, y));
            if (options.Variant == DetailingVariant.Straight)
                AddBreak(frame.Point(left, section.Reinforcement.Top + frame.Paper(2)), frame.Point(right, section.Reinforcement.Top + frame.Paper(2)), true);
            // Each side uses its own slab thickness and elevation. A slab on one
            // side must neither invent a break nor a level on the other side.
            foreach (bool atLeft in new[] { true, false })
            {
                var slabs = section.UpperSlabs.Where(h => atLeft ? h.Bounds.Left < left - 1 / 304.8 : h.Bounds.Right > right + 1 / 304.8);
                foreach (var slab in slabs)
                {
                    double slabBottom = Math.Max(slab.Bounds.Bottom, top - 1000 / 304.8), slabTop = slab.Bounds.Top;
                    double x = atLeft ? section.BreakLeft : section.BreakRight;
                    if (slabTop > slabBottom + 1 / 304.8)
                        AddBreak(frame.Point(x, slabBottom), frame.Point(x, slabTop), atLeft);
                }
            }
            double lowerY = section.BreakBottom;
            int lowerKind = DetailingSectionLayout.LowerBreakKind(section.LowerLeft, section.LowerRight);
            if (lowerKind == 1)
                AddBreak(frame.Point(left, lowerY), frame.Point(right, lowerY), false);
            else
                AddCornerBreak(section, lowerKind);
            if (section.UpperSlabs.Count > 0)
            {
                double slabTop = section.UpperSlabs.Max(h => h.Bounds.Top), slabBottom = section.UpperSlabs.Min(h => h.Bounds.Bottom);
                AddNote(options.UpperNoteTop, ResolveNote(options.UpperNoteBottom, section.UpperSlabs),
                    frame.Point(section.BreakRight + frame.Paper(15), slabTop + frame.Paper(5)), frame.Point((left + right) / 2, (slabBottom + slabTop) / 2));
            }
            if (section.LowerConcrete.Count > 0)
                AddNote(options.LowerNoteTop, ResolveNote(options.LowerNoteBottom, section.LowerConcrete),
                    frame.Point(section.BreakRight + frame.Paper(25), bottom - frame.Paper(12)), frame.Point((left + right) / 2, bottom - frame.Paper(2)));
        }

        string ResolveNote(string text, IEnumerable<DetailingHost> hosts)
        {
            string name = hosts.Where(h => h.Link != null).OrderBy(h => h.Link!.Name.Contains("_ALL") ? 1 : 0).FirstOrDefault()?.Link?.Name ?? "";
            var mark = System.Text.RegularExpressions.Regex.Match(name, @"КЖ\d+(?:\.\d+)*");
            return text.Replace("{том}", mark.Success ? mark.Value : "");
        }
        FamilySymbol? Symbol(int kind)
        {
            string family = "(Узел) Область обрыва • " + kind;
            var symbol = symbols.FirstOrDefault(s => s.FamilyName == family && s.Name == "M" + view.Scale);
            if (symbol == null) { result.Notes.Add("Для обрывов загрузите семейство «" + family + "» с типом M" + view.Scale + "."); return null; }
            if (!symbol.IsActive) symbol.Activate();
            return symbol;
        }
        void AddCornerBreak(DetailingSection section, int kind)
        {
            double left = section.LowerLeft ? section.BreakLeft : section.Wall.Left;
            double right = section.LowerRight ? section.BreakRight : section.Wall.Right;
            double y = section.BreakBottom;
            if (preserved.OfType<FamilyInstance>().Any(f => f.Symbol.FamilyName == "(Узел) Область обрыва • " + kind
                && f.Location is LocationPoint p && Math.Abs(frame.Y(p.Point) - y) < frame.Paper(12)
                && frame.X(p.Point) >= left - frame.Paper(10) && frame.X(p.Point) <= right + frame.Paper(10))) return;
            FamilySymbol? symbol = Symbol(kind);
            if (symbol == null) return;
            // Place the requested family directly. A random instance copied from
            // another view can carry a different rotation, handedness and lengths.
            XYZ point = frame.Point(right, y);
            FamilyInstance detail = doc.Create.NewFamilyInstance(point, symbol, view);
            SetLength(detail, "Длина линии 1", frame.Paper(6));
            SetLength(detail, "Длина линии 2", right - left);
            if (kind == 3) SetLength(detail, "Длина линии 3", frame.Paper(6));
            doc.Regenerate();
            XYZ axis = detail.GetTransform().BasisX;
            // In these families line 1 follows local X (the vertical return),
            // line 2 follows local Y (to the left in a bottom U/L break).
            double angle = Math.Atan2(frame.Normal.DotProduct(axis.CrossProduct(frame.Up)), axis.DotProduct(frame.Up));
            if (Math.Abs(angle) > 1e-8) ElementTransformUtils.RotateElement(doc, detail.Id, Line.CreateBound(point, point + frame.Normal), angle);
            // Family 2 has a single side return. Mirror the new annotation for
            // concrete on the right; the model and manual details stay untouched.
            if (kind == 2 && section.LowerRight)
                ElementTransformUtils.MirrorElements(doc, new[] { detail.Id }, Plane.CreateByNormalAndOrigin(frame.Right, frame.Point((left + right) / 2, y)), false);
            MarkDetail(detail, "break"); result.Details++;
        }
        void AddBreak(XYZ start, XYZ end, bool reverse)
        {
            string key = string.Join("|", new[] { frame.X(start), frame.Y(start), frame.X(end), frame.Y(end) }.Select(v => Math.Round(v * 304.8, 1)));
            if (!createdBreaks.Add(key)) return;
            XYZ middle = (start + end) / 2, direction = (end - start).Normalize();
            if (preserved.OfType<FamilyInstance>().Any(f => f.Symbol.FamilyName.StartsWith("(Узел) Область обрыва", StringComparison.Ordinal)
                && f.Location is LocationCurve curve && curve.Curve is Line line && Math.Abs(line.Direction.DotProduct(direction)) > 0.999
                && frame.Flatten(line.Evaluate(0.5, true)).DistanceTo(middle) < frame.Paper(2))) return;
            FamilySymbol? symbol = Symbol(1);
            if (symbol == null) return;
            FamilyInstance detail = doc.Create.NewFamilyInstance(Line.CreateBound(reverse ? end : start, reverse ? start : end), symbol, view);
            // The placement line already defines the length. Some family
            // versions expose it as read-only instead of a writable parameter.
            var length = detail.GetParameters("Длина").FirstOrDefault(p => p.StorageType == StorageType.Double && !p.IsReadOnly);
            length?.Set(start.DistanceTo(end));
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
        void AddNote(string upper, string lower, XYZ head, XYZ target)
        {
            if (string.IsNullOrWhiteSpace(upper) && string.IsNullOrWhiteSpace(lower)) return;
            if (preserved.OfType<FamilyInstance>().Any(f => f.Symbol.FamilyName == "(Аннотация) Выноска" && f.Location is LocationPoint p
                && frame.Flatten(p.Point).DistanceTo(head) < frame.Paper(15))
                || preserved.OfType<TextNote>().Any(n => n.Text.Trim() == (upper + "\n" + lower).Trim() && frame.Flatten(n.Coord).DistanceTo(head) < frame.Paper(15))) return;
            FamilySymbol? symbol = symbols.FirstOrDefault(s => s.FamilyName == "(Аннотация) Выноска" && s.Name == "• Без указателя");
            if (symbol == null) { result.Notes.Add("Для подписей плит загрузите семейство «(Аннотация) Выноска»."); return; }
            if (!symbol.IsActive) symbol.Activate();
            double width = Math.Max(20, Math.Max(upper.Length, lower.Length) * 1.5 + 2);
            FamilyInstance note = doc.Create.NewFamilyInstance(head, symbol, view);
            note.LookupParameter("Верхняя строка")?.Set(upper); note.LookupParameter("Нижняя строка")?.Set(lower);
            SetLength(note, "Длина полки", width / 304.8);
            if (note is AnnotationSymbol annotation)
            {
                annotation.addLeader(); doc.Regenerate();
                var leader = annotation.GetLeaders().Cast<Leader>().Last(); leader.End = target; leader.Elbow = head - frame.Right * frame.Paper(width / 2);
            }
            MarkDetail(note, "note"); result.Details++;
        }
    }

    private static void SetLength(Element element, string name, double value)
    {
        Parameter? parameter = element.GetParameters(name).FirstOrDefault(p => p.StorageType == StorageType.Double && !p.IsReadOnly);
        if (parameter == null || !parameter.Set(value)) throw new InvalidOperationException("Семейство «" + element.Name + "»: недоступен размер «" + name + "».");
    }
}
