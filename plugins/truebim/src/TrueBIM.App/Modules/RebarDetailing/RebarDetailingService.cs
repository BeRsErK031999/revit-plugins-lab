using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed partial class RebarDetailingService
{
    public static bool Supports(View view) => !view.IsTemplate && Math.Abs(view.ViewDirection.Z) < 0.001
        && Math.Abs(view.UpDirection.Z) > 0.999 && (view.ViewType == ViewType.Section
        || view.ViewType == ViewType.Detail || view.ViewType == ViewType.Elevation);

    public IReadOnlyList<View> ResolveViews(Document doc, ICollection<ElementId> selection, View active)
    {
        if (selection.Count == 0) return Supports(active) ? new[] { active } : Array.Empty<View>();
        return selection.Select(doc.GetElement).Select(e => e is Viewport port ? doc.GetElement(port.ViewId) as View : e as View)
            .Where(v => v != null && Supports(v)).Cast<View>().GroupBy(v => v.Id).Select(g => g.First()).ToList();
    }

    public IReadOnlyList<DetailingViewRow> Inspect(IEnumerable<View> views) => views.Select(view =>
    {
        DetailingViewRow row = new(view);
        try
        {
            DetailingGeometry geometry = DetailingGeometry.Read(view.Document, view);
            row.Bars = geometry.Bars.Count; row.Hosts = geometry.HostCount;
            var annotations = Annotations(view, new DetailingOptions());
            row.Existing = annotations.Count;
            if (!view.CropBoxActive) row.Status = "Включите обрезку вида перед оформлением";
            else if (row.Bars == 0) row.Status = "Нет видимой арматуры в границах сечения";
            else if (annotations.Any(e => e.Pinned || e.GroupId != ElementId.InvalidElementId || IsLocked(e))) row.Status = "Есть закреплённые, сгруппированные или заблокированные аннотации";
            else if (geometry.Notes.Count > 0) row.Status = string.Join("; ", geometry.Notes.Distinct());
            row.Selected = view.CropBoxActive && row.Bars > 0;
        }
        catch (Exception ex) { row.Status = ex.Message; row.Selected = false; }
        return row;
    }).ToList();

    public IReadOnlyList<DetailingType> Types(Document doc, string kind)
    {
        var elements = new FilteredElementCollector(doc).WhereElementIsElementType().ToElements();
        return elements.Where(e => kind switch
        {
            "tag" => e is FamilySymbol && e.Category?.Id == new ElementId(BuiltInCategory.OST_RebarTags),
            "dimension" => e is DimensionType d && d.StyleType == DimensionStyleType.Linear,
            "spot" => e is DimensionType d && d.StyleType == DimensionStyleType.SpotElevation,
            "multi" => e is MultiReferenceAnnotationType,
            _ => false
        }).Select(e => new DetailingType(e.Id, e is FamilySymbol s ? s.FamilyName + ": " + s.Name : e.Name))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public ElementId PreferredType(IEnumerable<View> views, IReadOnlyList<DetailingType> types, string kind)
    {
        if (kind == "dimension")
        {
            string rounded = "2.5 (окр. 5)";
            var standard = types.FirstOrDefault(t => t.Name.EndsWith(rounded, StringComparison.OrdinalIgnoreCase));
            if (standard != null) return standard.Id;
        }
        var valid = new HashSet<ElementId>(types.Select(t => t.Id));
        var counts = views.SelectMany(v => new FilteredElementCollector(v.Document, v.Id).WhereElementIsNotElementType()
            .Where(e => e.OwnerViewId == v.Id && valid.Contains(e.GetTypeId())))
            .GroupBy(e => e.GetTypeId()).OrderByDescending(g => g.Count()).ToList();
        if (counts.Count > 0) return counts[0].Key;
        string preferred = kind == "tag" ? "Марка/Шаг (Количество)" : kind == "multi" ? "Без марки" : kind == "spot" ? "ПСК" : "2.5";
        return types.FirstOrDefault(t => t.Name.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0)?.Id
            ?? types.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    public IReadOnlyList<DetailingResult> Apply(Document doc, IEnumerable<View> views, DetailingOptions options)
    {
        List<DetailingResult> results = new();
        using TransactionGroup group = new(doc, "TrueBIM · Оформление вертикальных сечений");
        group.Start();
        foreach (View view in views) results.Add(ApplyView(doc, view, options));
        group.Assimilate();
        return results;
    }

    private static DetailingResult ApplyView(Document doc, View view, DetailingOptions options)
    {
        DetailingResult result = new() { ViewName = view.Name };
        string stage = "Проверка вида";
        using Transaction transaction = new(doc, "Оформление: " + view.Name);
        try
        {
            if (!Supports(view) || !view.CropBoxActive) throw new InvalidOperationException("Нужно вертикальное сечение с включённой обрезкой. Горизонтальные и наклонные виды не оформляются.");
            DetailingGeometry geometry = DetailingGeometry.Read(doc, view);
            result.Notes.AddRange(geometry.Notes);
            if (geometry.Bars.Count == 0) throw new InvalidOperationException("В границах вида нет видимой системной арматуры.");
            List<Element> old = options.ReplaceExisting ? Annotations(view, options) : new();
            if (old.Any(e => e.Pinned || e.GroupId != ElementId.InvalidElementId || IsLocked(e)))
                throw new InvalidOperationException("Размеры или марки закреплены, сгруппированы либо заблокированы. Снимите защиту вручную или отключите замену аннотаций.");
            if (options.Tags && doc.GetElement(options.TagTypeId) is not FamilySymbol) throw new InvalidOperationException("Выберите семейство марки арматуры.");
            if ((options.ConcreteDimensions || options.BarDimensions) && doc.GetElement(options.DimensionTypeId) is not DimensionType)
                throw new InvalidOperationException("Выберите линейный размерный стиль.");
            DetailingFrame frame = new(view);
            ValidateVariant(frame, geometry, options);
            List<XYZ> points = geometry.Bars.SelectMany(b => b.Segments.SelectMany(s => new[] { s.Start, s.End })).ToList();
            if (points.Count == 0) points.AddRange(geometry.Bars.SelectMany(b => b.Positions));
            double minX = points.Min(frame.X), maxX = points.Max(frame.X), minY = points.Min(frame.Y), maxY = points.Max(frame.Y);
            var originalOwned = new HashSet<ElementId>(new FilteredElementCollector(doc).OwnedByView(view.Id).ToElementIds());
            transaction.Start();
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(new FailureHandler(result)).SetClearAfterRollback(true));
            if (old.Count > 0)
            {
                // Check the complete cascade. Deleting dimensions must never remove a model
                // element, a group, or annotation belonging to another view.
                var permitted = new HashSet<ElementId>(old.Select(e => e.Id));
                foreach (MultiReferenceAnnotation mra in old.OfType<MultiReferenceAnnotation>())
                foreach (ElementId child in new[] { mra.TagId, mra.DimensionId })
                {
                    Element element = doc.GetElement(child);
                    if (element == null) continue;
                    if (element.Pinned || element.GroupId != ElementId.InvalidElementId || IsLocked(element))
                        throw new InvalidOperationException("Мультиарматурная аннотация содержит закреплённый или заблокированный элемент.");
                    permitted.Add(child);
                }
                var deleted = doc.Delete(old.Select(e => e.Id).ToList());
                if (deleted.Any(id => !permitted.Contains(id))) throw new InvalidOperationException("Удаление затрагивает элементы вне оформления данного вида.");
                result.Removed = deleted.Count;
            }
            stage = "Обрывы и подписи плит";
            if (options.Details) Details(doc, view, frame, geometry, options, result);
            doc.Regenerate();
            stage = "Шаг и привязки арматуры";
            CombinedDimensions(doc, view, frame, geometry, options, result);
            doc.Regenerate();
            ArrangeDimensionText(frame, new FilteredElementCollector(doc).OwnedByView(view.Id).OfClass(typeof(Dimension))
                .Cast<Dimension>().Where(d => d is not SpotDimension && !originalOwned.Contains(d.Id)));
            doc.Regenerate();
            stage = "Марки арматуры";
            if (options.Tags) CompactTags(doc, view, frame, geometry, options, result);
            stage = "Высотные отметки";
            if (options.Elevations && frame.Vertical) Spots(doc, view, frame, geometry, options, minX, result);
            stage = "Замена аннотаций";
            if (result.Tags + result.Dimensions + result.Elevations + result.Details == 0) throw new InvalidOperationException("Нет доступных геометрических ссылок для оформления.");
            doc.Regenerate();
            stage = "Граница аннотаций";
            doc.Regenerate();
            var created = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType().Where(e => !originalOwned.Contains(e.Id)).ToList();
            ArrangeDimensionText(frame, created.OfType<Dimension>().Where(d => d is not SpotDimension));
            doc.Regenerate();
            ExtendAnnotationCrop(view, frame, created);
            if (transaction.Commit() != TransactionStatus.Committed) throw new InvalidOperationException("Revit отклонил оформление вида.");
            result.Succeeded = true;
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started) transaction.RollBack();
            result.Tags = result.Dimensions = result.Elevations = result.Removed = result.Details = result.QuantityWrites = 0;
            result.Notes.Add(stage + ": " + ex.Message);
        }
        return result;
    }

    private static bool IsLocked(Element e) => e is Dimension d && e is not SpotDimension
        && (d.NumberOfSegments > 1 ? d.AreSegmentsEqual || d.Segments.Cast<DimensionSegment>().Any(s => s.IsLocked) : d.IsLocked);

    private static List<Element> Annotations(View view, DetailingOptions options)
    {
        var owned = new FilteredElementCollector(view.Document).OwnedByView(view.Id).WhereElementIsNotElementType().ToList();
        var multi = owned.OfType<MultiReferenceAnnotation>().ToList();
        var children = new HashSet<ElementId>(multi.SelectMany(m => new[] { m.TagId, m.DimensionId }));
        return owned.Where(e => !children.Contains(e.Id) && (OwnedDetail(e) ? options.Details : e is SpotDimension ? options.Elevations
            : e is Dimension dimension ? IsRebarDimension(dimension) ? options.BarDimensions : options.ConcreteDimensions
            : e is IndependentTag ? options.Tags && e.Category?.Id == new ElementId(BuiltInCategory.OST_RebarTags)
            : e is MultiReferenceAnnotation && options.BarDimensions && options.Tags)).ToList();
    }

    private static bool IsRebarDimension(Dimension dimension) => dimension.References.Cast<Reference>().Any(reference =>
    {
        Element element = dimension.Document.GetElement(reference.ElementId);
        if (element is RevitLinkInstance link) element = link.GetLinkDocument()?.GetElement(reference.LinkedElementId)!;
        return element is Rebar || element is RebarInSystem;
    });

    private static (double MinX, double MinY, double MaxX, double MaxY)? Bounds(Element element, View view, DetailingFrame frame)
    {
        BoundingBoxXYZ? box = element.get_BoundingBox(view);
        if (box == null) return null;
        var points = new List<XYZ>();
        for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
            points.Add(box.Transform.OfPoint(new XYZ(x == 0 ? box.Min.X : box.Max.X, y == 0 ? box.Min.Y : box.Max.Y, z == 0 ? box.Min.Z : box.Max.Z)));
        return (points.Min(frame.X), points.Min(frame.Y), points.Max(frame.X), points.Max(frame.Y));
    }

    private static void ExtendAnnotationCrop(View view, DetailingFrame frame, IEnumerable<Element> annotations)
    {
        if (view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE)?.AsInteger() != 1) return;
        using ViewCropRegionShapeManager manager = view.GetCropRegionShapeManager();
        if (!manager.CanHaveAnnotationCrop) return;
        BoundingBoxXYZ crop = view.CropBox;
        XYZ a = crop.Transform.OfPoint(crop.Min), b = crop.Transform.OfPoint(crop.Max);
        double minX = Math.Min(frame.X(a), frame.X(b)), maxX = Math.Max(frame.X(a), frame.X(b));
        double minY = Math.Min(frame.Y(a), frame.Y(b)), maxY = Math.Max(frame.Y(a), frame.Y(b));
        var boxes = annotations.Select(e => Bounds(e, view, frame)).Where(box => box.HasValue).Select(box => box!.Value).ToList();
        if (boxes.Count == 0) return;
        double margin = frame.Paper(3);
        manager.LeftAnnotationCropOffset = Math.Max(manager.LeftAnnotationCropOffset, (minX - boxes.Min(box => box.MinX) + margin) / view.Scale);
        manager.RightAnnotationCropOffset = Math.Max(manager.RightAnnotationCropOffset, (boxes.Max(box => box.MaxX) - maxX + margin) / view.Scale);
        manager.BottomAnnotationCropOffset = Math.Max(manager.BottomAnnotationCropOffset, (minY - boxes.Min(box => box.MinY) + margin) / view.Scale);
        manager.TopAnnotationCropOffset = Math.Max(manager.TopAnnotationCropOffset, (boxes.Max(box => box.MaxY) - maxY + margin) / view.Scale);
    }

    private sealed class DimensionLabel
    {
        public XYZ Position { get; set; } = null!;
        public XYZ Origin { get; set; } = null!;
        public double Length { get; set; }
        public double Width { get; set; }
        public Action<XYZ> Move { get; set; } = null!;
    }

    private static void ArrangeDimensionText(DetailingFrame frame, IEnumerable<Dimension> dimensions)
    {
        var all = dimensions.Where(d => d.Curve is Line).ToList();
        var numeric = all.Where(d => d.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ)?.AsInteger() != 2).ToList();
        foreach (var row in numeric.GroupBy(d =>
        {
            var line = (Line)d.Curve;
            bool horizontal = Math.Abs(line.Direction.DotProduct(frame.Right)) > 0.999;
            return (Horizontal: horizontal, Lane: Math.Round((horizontal ? frame.Y(line.Origin) : frame.X(line.Origin)) * 304.8, 1));
        }))
        {
            XYZ direction = row.Key.Horizontal ? frame.Right : frame.Up;
            XYZ across = row.Key.Horizontal ? frame.Up : frame.Right;
            var formulas = all.Where(d => d.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ)?.AsInteger() == 2
                && Math.Abs(((Line)d.Curve).Direction.DotProduct(direction)) > 0.999
                && Math.Round((row.Key.Horizontal ? frame.Y(((Line)d.Curve).Origin) : frame.X(((Line)d.Curve).Origin)) * 304.8, 1) == row.Key.Lane)
                .Select(d =>
                {
                    var segments = d.Segments.Cast<DimensionSegment>().ToList();
                    string text = segments.Count + "×" + Math.Round((segments.FirstOrDefault()?.Value ?? 0) * 304.8) + "="
                        + Math.Round(segments.Sum(s => s.Value ?? 0) * 304.8);
                    double height = d.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() * frame.View.Scale ?? frame.Paper(2.5);
                    return new { Center = segments.Count == 0 ? 0 : (segments.First().Origin + segments.Last().Origin).DotProduct(direction) / 2,
                        Width = text.Length * height * 0.65 };
                }).ToList();
            var labels = new List<DimensionLabel>();
            foreach (var dimension in row)
            {
                double textHeight = dimension.DimensionType.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() * frame.View.Scale ?? frame.Paper(2.5);
                double Width(double value) => Math.Max(frame.Paper(2), Math.Round(value * 304.8).ToString(System.Globalization.CultureInfo.InvariantCulture).Length * textHeight * 0.65);
                if (dimension.NumberOfSegments < 2)
                {
                    if (!dimension.IsTextPositionAdjustable() || !dimension.Value.HasValue) continue;
                    labels.Add(new DimensionLabel { Position = dimension.TextPosition, Origin = dimension.Origin,
                        Length = dimension.Value.Value, Width = Width(dimension.Value.Value), Move = p => dimension.TextPosition = p });
                    continue;
                }
                foreach (DimensionSegment segment in dimension.Segments)
                {
                    if (!segment.IsTextPositionAdjustable() || !segment.Value.HasValue) continue;
                    labels.Add(new DimensionLabel { Position = segment.TextPosition, Origin = segment.Origin,
                        Length = segment.Value.Value, Width = Width(segment.Value.Value), Move = p => segment.TextPosition = p });
                }
            }
            labels = labels.OrderBy(l => l.Origin.DotProduct(direction)).ToList();
            var centers = labels.Select(l => l.Position.DotProduct(direction)).ToArray();
            for (int index = 0; index < labels.Count; index++)
            {
                var label = labels[index];
                if (label.Length >= label.Width + frame.Paper(0.5)) continue;
                if (index == 0) centers[index] = label.Origin.DotProduct(direction) - label.Length / 2 - label.Width / 2 - frame.Paper(1);
                else if (index == labels.Count - 1) centers[index] = label.Origin.DotProduct(direction) + label.Length / 2 + label.Width / 2 + frame.Paper(1);
            }
            // Cover and transition dimensions are separate native objects, but
            // their labels must be spaced together with the adjoining numeric run.
            for (int index = centers.Length - 2; index >= 0; index--)
                centers[index] = Math.Min(centers[index], centers[index + 1] - (labels[index].Width + labels[index + 1].Width) / 2 - frame.Paper(1.2));
            for (int index = 0; index < labels.Count; index++)
            {
                var label = labels[index];
                double shift = centers[index] - label.Position.DotProduct(direction);
                XYZ position = label.Position + direction * shift;
                // A short cover/transition label may overlap an adjacent native
                // equality formula. Move its text across the row, keeping the
                // dimension line at its required offset and the formula intact.
                if (formulas.Any(f => Math.Abs(centers[index] - f.Center) < (label.Width + f.Width) / 2 + frame.Paper(1.2)))
                    position += across * (frame.Paper(4) - (position - label.Origin).DotProduct(across));
                if (position.DistanceTo(label.Position) > frame.Paper(0.01)) label.Move(position);
            }
        }
    }
    private static Reference ResolveAxis(Document doc, View view, DetailingAxis axis, DetailingFace? face, XYZ origin, XYZ direction, ElementId type, DetailingAxis? anchor = null)
    {
        // Materialize pending family/tag edits before entering a rollback probe.
        // Revit may otherwise discard their deferred geometry evaluation.
        doc.Regenerate();
        if (face == null)
        {
            if (anchor != null)
            foreach (Reference candidate in axis.Candidates)
            foreach (Reference anchorCandidate in anchor.Candidates)
            {
                using SubTransaction probe = new(doc); probe.Start();
                try
                {
                    Dimension dimension = CreateDimension(doc, view, origin, direction, new[] { candidate, anchorCandidate }, type);
                    doc.Regenerate();
                    bool valid = dimension.Value.HasValue && Math.Abs(dimension.Value.Value - Math.Abs((axis.Point - anchor.Point).DotProduct(direction))) < 0.1 / 304.8;
                    probe.RollBack(); if (valid) return candidate;
                }
                catch { if (probe.GetStatus() == TransactionStatus.Started) probe.RollBack(); }
            }
            throw new InvalidOperationException($"Стержень {axis.Reference.ElementId}: Revit не подтвердил расстояние между осями стержней.");
        }
        double expected = Math.Abs((axis.Point - face.Point).DotProduct(direction));
        var measured = new List<string>();
        foreach (Reference candidate in axis.Candidates)
        {
            using SubTransaction probe = new(doc); probe.Start();
            try
            {
                Dimension dimension = CreateDimension(doc, view, origin, direction, new[] { face.Reference, candidate }, type);
                doc.Regenerate();
                measured.Add(dimension.Value.HasValue ? (dimension.Value.Value * 304.8).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "null");
                bool centerline = dimension.Value.HasValue && Math.Abs(dimension.Value.Value - expected) < 0.1 / 304.8;
                probe.RollBack();
                if (centerline) return candidate;
            }
            catch (Exception ex) { measured.Add(ex.Message); if (probe.GetStatus() == TransactionStatus.Started) probe.RollBack(); }
        }
        // Some joined-wall face/point pairs require a second bar point for Revit
        // to infer the dimension plane. The intermediate point cancels out when
        // measuring the span between the face and the candidate axis.
        if (anchor != null && Math.Abs((anchor.Point - axis.Point).DotProduct(direction)) > 1.0 / 304.8)
        foreach (Reference candidate in axis.Candidates)
        foreach (Reference anchorCandidate in anchor.Candidates)
        {
            using SubTransaction probe = new(doc); probe.Start();
            try
            {
                Dimension dimension = CreateDimension(doc, view, origin, direction, new[] { face.Reference, candidate, anchorCandidate }, type);
                doc.Regenerate();
                var lengths = dimension.Segments.Cast<DimensionSegment>().Select(s => s.Value ?? double.NaN).ToList();
                var order = new[] { (Index: 0, Value: face.Point.DotProduct(direction)), (Index: 1, Value: axis.Point.DotProduct(direction)),
                    (Index: 2, Value: anchor.Point.DotProduct(direction)) }.OrderBy(p => p.Value).Select(p => p.Index).ToList();
                int start = Math.Min(order.IndexOf(0), order.IndexOf(1)), end = Math.Max(order.IndexOf(0), order.IndexOf(1));
                bool centerline = lengths.Count == 2 && Math.Abs(lengths.Skip(start).Take(end - start).Sum() - expected) < 0.1 / 304.8;
                probe.RollBack();
                if (centerline) return candidate;
            }
            catch { if (probe.GetStatus() == TransactionStatus.Started) probe.RollBack(); }
        }
        throw new InvalidOperationException($"Стержень {axis.Reference.ElementId}: Revit не подтвердил осевую привязку {expected * 304.8:0.###} мм (измерено: {string.Join(", ", measured)}). Грань {face.Reference.ConvertToStableRepresentation(doc)}, ось {axis.Reference.ConvertToStableRepresentation(doc)}.");
    }

    private static Dimension CreateDimension(Document doc, View view, XYZ origin, XYZ direction, IEnumerable<Reference> references, ElementId type)
    {
        ReferenceArray array = new();
        // Native geometry handles may be invalidated by a rolled-back measurement.
        // Rehydrate stable references in the current document state for every use.
        foreach (Reference reference in references)
            array.Append(Reference.ParseFromStableRepresentation(doc, reference.ConvertToStableRepresentation(doc)));
        Dimension dimension = doc.Create.NewDimension(view, Line.CreateBound(origin - direction * 100, origin + direction * 100), array, (DimensionType)doc.GetElement(type));
        if (dimension == null) throw new InvalidOperationException("Revit не создал размер по выбранным геометрическим ссылкам.");
        return dimension;
    }

    private static void Spots(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options,
        double minX, DetailingResult result)
    {
        if (doc.GetElement(options.SpotTypeId) is not DimensionType) throw new InvalidOperationException("Выберите тип высотной отметки.");
        foreach (var section in Sections(frame, geometry, options))
        {
            var levels = section.Faces.Where(f => !f.Horizontal && Math.Abs(f.Normal.Z) > 0.999).OrderBy(f => frame.Y(f.Point)).ToList();
            if (levels.Count == 0) continue;
            var selected = new List<DetailingFace> { levels.First() };
            var keys = new HashSet<string>(section.UpperSlabs.Select(h => h.Key));
            var slabLevels = geometry.Faces.Where(f => keys.Contains(f.HostKey) && !f.Horizontal && Math.Abs(f.Normal.Z) > 0.999)
                .OrderBy(f => frame.Y(f.Point)).ToList();
            if (slabLevels.Count > 0) { selected.Add(slabLevels.First()); selected.Add(slabLevels.Last()); }
            else selected.Add(levels.Last());
            selected = selected.GroupBy(f => Math.Round(f.Point.Z * 304.8, 1)).Select(g => g.First()).OrderBy(f => f.Point.Z).ToList();
            var layout = SectionAnnotationBoundary(doc, view, frame, section);
            double lastY = double.NegativeInfinity;
            foreach (var face in selected)
            {
                double y = Math.Max(frame.Y(face.Point), lastY + frame.Paper(5)); lastY = y;
                XYZ origin = frame.Flatten(face.Point);
                XYZ end = frame.Point(layout.Left - frame.Paper(24), y), bend = end + frame.Right * frame.Paper(10);
                SpotDimension spot = doc.Create.NewSpotElevation(view, Reference.ParseFromStableRepresentation(doc, face.Reference.ConvertToStableRepresentation(doc)), origin, bend, end, face.Point, true);
                bool upperSurface = face == selected.First() || (slabLevels.Count > 0 && Math.Abs(face.Point.Z - slabLevels.Last().Point.Z) < 0.1 / 304.8);
                string style = upperSurface ? "(ПСК) Стрелка Вверх • 5" : "(ПСК) Стрелка Вниз • 5";
                var standard = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                    .FirstOrDefault(t => t.StyleType == DimensionStyleType.SpotElevation && t.Name == style);
                spot.ChangeTypeId(standard?.Id ?? options.SpotTypeId); result.Elevations++;
            }
        }
        if (result.Elevations == 0) result.Notes.Add("Высотные отметки: в обрезке нет горизонтальных граней бетона.");
    }
    private sealed class FailureHandler : IFailuresPreprocessor
    {
        private readonly DetailingResult result;
        public FailureHandler(DetailingResult result) { this.result = result; }
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                result.Notes.Add(failure.GetDescriptionText());
                if (failure.GetSeverity() == FailureSeverity.Error) return FailureProcessingResult.ProceedWithRollBack;
                accessor.DeleteWarning(failure);
            }
            return FailureProcessingResult.Continue;
        }
    }
}
