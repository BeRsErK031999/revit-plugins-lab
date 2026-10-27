using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace TrueBIM.App.Modules.RebarDetailing;

public sealed class RebarDetailingService
{
    public static bool Supports(View view) => !view.IsTemplate && (view.ViewType == ViewType.Section
        || view.ViewType == ViewType.Detail || view.ViewType == ViewType.Elevation
        || view.ViewType == ViewType.FloorPlan || view.ViewType == ViewType.EngineeringPlan);

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
        using TransactionGroup group = new(doc, "TrueBIM · Оформление сечений");
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
            if (!Supports(view) || !view.CropBoxActive) throw new InvalidOperationException("Нужен разрез, фасад или план с включённой обрезкой.");
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
            List<XYZ> points = geometry.Bars.SelectMany(b => b.Positions).Concat(geometry.Faces.Select(f => f.Point)).ToList();
            double minX = points.Min(frame.X), maxX = points.Max(frame.X), minY = points.Min(frame.Y), maxY = points.Max(frame.Y);
            DetailingLanes horizontalLanes = new(frame.Paper(8)), verticalLanes = new(frame.Paper(8));
            var originalOwned = new HashSet<ElementId>(new FilteredElementCollector(doc).OwnedByView(view.Id).ToElementIds());
            transaction.Start();
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(new FailureHandler(result)).SetClearAfterRollback(true));
            stage = "Размеры бетона";
            if (options.ConcreteDimensions) Concrete(doc, view, frame, geometry, options, minX, minY, horizontalLanes, verticalLanes, result);
            stage = "Шаг и привязки арматуры";
            if (options.BarDimensions)
            {
                BarDimensions(doc, view, frame, geometry, options, minX, minY, horizontalLanes, verticalLanes, result);
                CrossSectionDimensions(doc, view, frame, geometry, options, minX, minY, horizontalLanes, verticalLanes, result);
            }
            stage = "Марки арматуры";
            if (options.Tags) Tags(doc, view, frame, geometry, options, minX, maxX, minY, verticalLanes.Count, result);
            stage = "Высотные отметки";
            if (options.Elevations && frame.Vertical) Spots(doc, view, frame, geometry, options, minX - frame.Paper(8 * verticalLanes.Count), result);
            stage = "Замена аннотаций";
            if (result.Tags + result.Dimensions + result.Elevations == 0) throw new InvalidOperationException("Нет доступных геометрических ссылок для оформления.");
            doc.Regenerate();
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
            result.Tags = result.Dimensions = result.Elevations = result.Removed = 0;
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
        return owned.Where(e => !children.Contains(e.Id) && (e is SpotDimension ? options.Elevations
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

    private static void Concrete(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options,
        double minX, double minY, DetailingLanes horizontalLanes, DetailingLanes verticalLanes, DetailingResult result)
    {
        var seen = new HashSet<string>();
        foreach (var host in geometry.Faces.GroupBy(f => f.HostKey))
        foreach (bool horizontal in new[] { true, false })
        {
            var faces = host.Where(f => f.Horizontal == horizontal).OrderBy(f => Coordinate(f.Point)).ToList();
            faces = faces.GroupBy(f => Math.Round(Coordinate(f.Point) * 304.8)).Select(g => g.First()).ToList();
            if (faces.Count < 2) continue;
            string key = horizontal + ":" + string.Join(",", faces.Select(f => Math.Round(Coordinate(f.Point) * 304.8)));
            if (!seen.Add(key)) continue;
            int laneIndex = (horizontal ? horizontalLanes : verticalLanes).Reserve(Coordinate(faces.First().Point), Coordinate(faces.Last().Point));
            double lane = frame.Paper(7 + 8 * laneIndex);
            XYZ origin = horizontal ? frame.Point(Coordinate(faces[0].Point), minY - lane) : frame.Point(minX - lane, Coordinate(faces[0].Point));
            CreateDimension(doc, view, origin, horizontal ? frame.Right : frame.Up, faces.Select(f => f.Reference), options.DimensionTypeId);
            result.Dimensions++;
            double Coordinate(XYZ p) => horizontal ? frame.X(p) : frame.Y(p);
        }
        if (result.Dimensions == 0) result.Notes.Add("Размеры бетона: нет параллельных граням вида опорных плоскостей в границах сечения.");
    }

    private static void Tags(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options,
        double minX, double maxX, double minY, int verticalLanes, DetailingResult result)
    {
        var groups = geometry.Bars.GroupBy(b => b.Key).Select(g => g.OrderBy(b => frame.Y(b.Target)).ElementAt(g.Count() / 2)).ToList();
        List<(DetailingBar Bar, IndependentTag Tag)> candidates = new();
        foreach (DetailingBar bar in groups)
        {
            try { candidates.Add((bar, IndependentTag.Create(doc, options.TagTypeId, view.Id, bar.Reference, false, TagOrientation.Horizontal, frame.Flatten(bar.Target)))); }
            catch (Exception ex) { throw new InvalidOperationException($"Стержень {bar.Element.Id} ({bar.Element.GetType().Name}): {ex.Message}", ex); }
        }
        doc.Regenerate();
        // Some plan representations show only one bar from a set. A valid tag on a
        // different subelement then exists but is invisible. Resolve that in the view.
        for (int index = 0; index < candidates.Count; index++)
        {
            var pair = candidates[index];
            if (new FilteredElementCollector(doc, view.Id).ToElementIds().Contains(pair.Tag.Id)) continue;
            foreach (Reference reference in pair.Bar.TagReferences)
            {
                IndependentTag alternative = IndependentTag.Create(doc, options.TagTypeId, view.Id, reference, false, TagOrientation.Horizontal, frame.Flatten(pair.Bar.Target));
                doc.Regenerate();
                if (new FilteredElementCollector(doc, view.Id).ToElementIds().Contains(alternative.Id))
                {
                    doc.Delete(pair.Tag.Id); pair.Bar.Reference = reference;
                    candidates[index] = (pair.Bar, alternative); break;
                }
                doc.Delete(alternative.Id);
            }
        }
        List<(DetailingBar Bar, IndependentTag Tag)> labels = new();
        foreach (var group in candidates.GroupBy(pair => DetailingGeometry.Key(pair.Bar.Host, pair.Bar.Link) + "|" + pair.Tag.TagText))
        {
            labels.Add(group.First());
            foreach (var duplicate in group.Skip(1)) doc.Delete(duplicate.Tag.Id);
        }
        var obstacles = new FilteredElementCollector(doc).OwnedByView(view.Id).WhereElementIsNotElementType()
            .Where(e => e is TextNote || e is AnnotationSymbol)
            .Select(e => Bounds(e, view, frame)).Where(b => b.HasValue).Select(b => b!.Value).ToList();
        double center = (minX + maxX) / 2;
        var onLeft = new HashSet<ElementId>();
        int leftCount = 0, rightCount = 0, limit = (labels.Count + 1) / 2;
        foreach (var pair in labels.OrderBy(p => frame.Y(p.Bar.Target)).ThenBy(p => frame.X(p.Bar.Target)))
        {
            bool preferLeft = frame.X(pair.Bar.Target) <= center;
            bool left = preferLeft ? leftCount < limit : rightCount >= limit;
            if (left) { onLeft.Add(pair.Tag.Id); leftCount++; } else rightCount++;
        }
        foreach (bool left in new[] { true, false })
        {
            var bars = labels.Where(p => onLeft.Contains(p.Tag.Id) == left).OrderBy(p => frame.Y(p.Bar.Target)).ThenBy(p => frame.X(p.Bar.Target)).ToList();
            double lastY = minY - frame.Paper(6);
            foreach (var pair in bars)
            {
                DetailingBar bar = pair.Bar; IndependentTag tag = pair.Tag;
                var box = Bounds(tag, view, frame) ?? (MinX: frame.X(bar.Target) - frame.Paper(10), MinY: frame.Y(bar.Target), MaxX: frame.X(bar.Target) + frame.Paper(10), MaxY: frame.Y(bar.Target) + frame.Paper(5));
                double relativeMinX = box.MinX - frame.X(tag.TagHeadPosition), relativeMaxX = box.MaxX - frame.X(tag.TagHeadPosition);
                // Revit can include a hidden leader/host extent in a plan tag's box.
                // Constrain those extents to the label area before assigning rows.
                double height = Math.Max(frame.Paper(5), Math.Min(box.MaxY - box.MinY, frame.Paper(9)));
                double relativeMinY = Math.Max(-height, Math.Min(box.MinY - frame.Y(tag.TagHeadPosition), 0));
                double x = left ? minX - frame.Paper(12 + 8 * verticalLanes) - relativeMaxX : maxX + frame.Paper(12) - relativeMinX;
                double leaderMinX = Math.Min(x + relativeMinX, frame.X(bar.Target)), leaderMaxX = Math.Max(x + relativeMaxX, frame.X(bar.Target));
                double bottom = Math.Max(frame.Y(bar.Target) - height / 2, lastY + frame.Paper(3));
                for (int attempt = 0; attempt <= obstacles.Count; attempt++)
                {
                    var hits = obstacles.Where(b => leaderMaxX + frame.Paper(2) > b.MinX && leaderMinX - frame.Paper(2) < b.MaxX
                        && bottom + height + frame.Paper(2) > b.MinY && bottom - frame.Paper(2) < b.MaxY).ToList();
                    if (hits.Count == 0) break;
                    bottom = hits.Max(b => b.MaxY) + frame.Paper(5);
                }
                double y = bottom - relativeMinY;
                lastY = bottom + height;
                obstacles.Add((x + relativeMinX, bottom, x + relativeMaxX, bottom + height));
                XYZ head = frame.Point(x, y);
                tag.HasLeader = true;
                tag.TagHeadPosition = head;
                tag.LeaderEndCondition = LeaderEndCondition.Free;
#if REVIT2022_OR_GREATER
                tag.SetLeaderEnd(bar.Reference, frame.Flatten(bar.Target));
                tag.SetLeaderElbow(bar.Reference, frame.Point(left ? minX - frame.Paper(3) : maxX + frame.Paper(3), y));
#else
                tag.LeaderEnd = frame.Flatten(bar.Target);
                tag.LeaderElbow = frame.Point(left ? minX - frame.Paper(3) : maxX + frame.Paper(3), y);
#endif
                result.Tags++;
            }
        }
    }

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

    private static void ArrangeDimensionText(DetailingFrame frame, IEnumerable<Dimension> dimensions)
    {
        foreach (Dimension dimension in dimensions)
        {
            if (dimension.Curve is not Line line || dimension.NumberOfSegments < 2) continue;
            var segments = dimension.Segments.Cast<DimensionSegment>().ToList();
            for (int index = 0; index < segments.Count; index++)
            {
                DimensionSegment segment = segments[index];
                if (!segment.IsTextPositionAdjustable() || !segment.Value.HasValue) continue;
                string text = segment.Prefix + Math.Round(segment.Value.Value * 304.8).ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (segment.Value.Value > frame.Paper(1.5 * text.Length + 1)) continue;
                XYZ shift = index == 0 ? -line.Direction * frame.Paper(4)
                    : index == segments.Count - 1 ? line.Direction * frame.Paper(4)
                    : line.Direction.CrossProduct(frame.Normal) * frame.Paper(3);
                segment.TextPosition += shift;
            }
        }
    }

    private static void BarDimensions(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options,
        double minX, double minY, DetailingLanes horizontalLanes, DetailingLanes verticalLanes, DetailingResult result)
    {
        var seen = new HashSet<string>();
        foreach (DetailingBar bar in geometry.Bars)
        {
            if (bar.Link != null || bar.Positions.Count < 2 || (bar.Element is Rebar rebar && !rebar.IsRebarShapeDriven())) continue;
            XYZ delta = bar.Distribution;
            XYZ projected = frame.Right * delta.DotProduct(frame.Right) + frame.Up * delta.DotProduct(frame.Up);
            if (projected.GetLength() < 1.0 / 304.8) continue;
            XYZ direction = projected.Normalize();
            bool horizontal = Math.Abs(direction.DotProduct(frame.Right)) > 0.999;
            if (!horizontal && Math.Abs(direction.DotProduct(frame.Up)) < 0.999) { result.Notes.Add("Наклонные ряды арматуры: расставлены марки; размер шага требует ручной проверки."); continue; }
            direction = horizontal ? frame.Right : frame.Up;
            var coordinates = bar.Positions.Select(p => p.DotProduct(direction)).OrderBy(x => x).ToList();
            string key = DetailingGeometry.Key(bar.Host, bar.Link) + ":" + horizontal + ":" + string.Join(",", coordinates.Select(x => Math.Round(x * 304.8)));
            if (!seen.Add(key)) continue;
            var hostFaces = geometry.Faces.Where(f => f.HostKey == DetailingGeometry.Key(bar.Host, bar.Link) && f.Horizontal == horizontal).ToList();
            DetailingFace? startFace = hostFaces.Where(f => f.Point.DotProduct(direction) < coordinates.First() - 1.0 / 304.8)
                .OrderByDescending(f => f.Point.DotProduct(direction)).FirstOrDefault();
            DetailingFace? endFace = hostFaces.Where(f => f.Point.DotProduct(direction) > coordinates.Last() + 1.0 / 304.8)
                .OrderBy(f => f.Point.DotProduct(direction)).FirstOrDefault();
            int laneIndex = (horizontal ? horizontalLanes : verticalLanes).Reserve(
                (startFace?.Point.DotProduct(direction) ?? coordinates.First()) - frame.Origin.DotProduct(direction),
                (endFace?.Point.DotProduct(direction) ?? coordinates.Last()) - frame.Origin.DotProduct(direction));
            double lane = frame.Paper(7 + 8 * laneIndex);
            XYZ origin = horizontal ? frame.Point(frame.X(bar.Target), minY - lane) : frame.Point(minX - lane, frame.Y(bar.Target));
            MultiReferenceAnnotation? mra = null;
            List<Reference> references;
            List<double> lengths;
            if (bar.DimensionReferences.Count == bar.Positions.Count)
            {
                var ordered = bar.DimensionReferences.OrderBy(p => p.Point.DotProduct(direction)).ToList();
                lengths = ordered.Skip(1).Select((p, i) => (p.Point - ordered[i].Point).DotProduct(direction)).ToList();
                bool uniform = lengths.Count > 1 && lengths.All(length => Math.Abs(length - lengths[0]) < 0.1 / 304.8);
                var axes = uniform ? new List<DetailingAxis> { ordered.First(), ordered.Last() } : ordered;
                references = axes.Select((p, index) => ResolveAxis(doc, view, p, startFace ?? endFace, origin, direction,
                    options.DimensionTypeId, axes[index == 0 ? axes.Count - 1 : 0])).ToList();
            }
            else
            {
                if (doc.GetElement(options.MultiTypeId) is not MultiReferenceAnnotationType multiType)
                    throw new InvalidOperationException("Для этого набора нужен загруженный тип мультиарматурной аннотации.");
                MultiReferenceAnnotationOptions mraOptions = new(multiType)
                { DimensionLineOrigin = origin, DimensionLineDirection = direction, DimensionPlaneNormal = frame.Normal,
                    DimensionStyleType = DimensionStyleType.Linear, TagHasLeader = false, TagHeadPosition = origin };
                mraOptions.SetElementsToDimension(new[] { bar.Element.Id });
                if (!MultiReferenceAnnotation.AreReferencesValidForLinearDimension(doc, view.Id, mraOptions))
                    mraOptions.DimensionStyleType = DimensionStyleType.LinearFixed;
                // A whole-set MRA would also dimension bars outside the view crop.
                int total = bar.Element is Rebar local ? local.NumberOfBarPositions : ((RebarInSystem)bar.Element).NumberOfBarPositions;
                if (bar.Positions.Count != total)
                    throw new InvalidOperationException($"Стержень {bar.Element.Id}: нет осевых ссылок на видимую часть набора.");
                try { mra = MultiReferenceAnnotation.Create(doc, view.Id, mraOptions); }
                catch (Exception ex) { throw new InvalidOperationException($"Стержень {bar.Element.Id} ({bar.Element.GetType().Name}): {ex.Message}", ex); }
                doc.Regenerate();
                Dimension original = (Dimension)doc.GetElement(mra.DimensionId);
                references = original.References.Cast<Reference>().ToList();
                lengths = original.NumberOfSegments > 1 ? original.Segments.Cast<DimensionSegment>().Select(s => s.Value ?? 0).ToList() : new List<double> { original.Value ?? 0 };
            }
            if (references.Count < 2) throw new InvalidOperationException($"Арматура {bar.Element.Id}: Revit не вернул опорные ссылки для размера шага.");
            // Keep dimensions associative. A constant pitch is shown as N x pitch = measured span;
            // its measured value is never replaced with a manually calculated number.
            bool equal = lengths.Count > 1 && lengths.All(length => Math.Abs(length - lengths[0]) < 0.1 / 304.8);
            var selected = equal ? new List<Reference> { references.First(), references.Last() } : references;
            if (startFace != null) selected.Insert(0, startFace.Reference);
            if (endFace != null) selected.Add(endFace.Reference);
            Dimension dimension = CreateDimension(doc, view, origin, direction, selected, options.DimensionTypeId);
            if (equal)
            {
                string prefix = lengths.Count + "×" + Math.Round(lengths[0] * 304.8, 1).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "=";
                if (startFace == null && endFace == null) dimension.Prefix = prefix;
                else
                {
                    doc.Regenerate();
                    double span = lengths.Sum();
                    DimensionSegment? middle = dimension.Segments.Cast<DimensionSegment>().FirstOrDefault(s => s.Value.HasValue && Math.Abs(s.Value.Value - span) < 0.1 / 304.8);
                    if (middle != null) middle.Prefix = prefix;
                }
            }
            if (mra != null) doc.Delete(mra.Id);
            result.Dimensions++;
        }
        if (geometry.Bars.Any(b => b.Link != null))
            result.Notes.Add("Арматура связей: марки расставлены; размерные цепочки шага создаются для арматуры текущего проекта.");
    }

    private static void CrossSectionDimensions(Document doc, View view, DetailingFrame frame, DetailingGeometry geometry, DetailingOptions options,
        double minX, double minY, DetailingLanes horizontalLanes, DetailingLanes verticalLanes, DetailingResult result)
    {
        // In a cross-section, longitudinal bars appear as points. Bind their two
        // outer axes to the concrete faces across the narrow section dimension.
        var seen = new HashSet<string>();
        foreach (var host in geometry.Bars.Where(b => b.Link == null).GroupBy(b => DetailingGeometry.Key(b.Host, b.Link)))
        foreach (bool horizontal in new[] { true, false })
        {
            XYZ direction = horizontal ? frame.Right : frame.Up;
            var faces = geometry.Faces.Where(f => f.HostKey == host.Key && f.Horizontal == horizontal)
                .OrderBy(f => f.Point.DotProduct(direction)).ToList();
            if (faces.Count < 2) continue;
            double low = faces.First().Point.DotProduct(direction), high = faces.Last().Point.DotProduct(direction);
            if (high - low > 1000.0 / 304.8) continue;
            var groups = host.GroupBy(b => b.Element.GetTypeId()).Select(g => g.SelectMany(b => b.DimensionReferences)
                .Where(r => Math.Abs(r.Direction.DotProduct(frame.Vertical && horizontal ? frame.Up : frame.Normal)) > 0.999
                    && r.Point.DotProduct(direction) > low + 1.0 / 304.8 && r.Point.DotProduct(direction) < high - 1.0 / 304.8)
                .OrderBy(r => Math.Round(r.Point.DotProduct(direction) * 304.8, 3))
                .ThenBy(r => r.Reference.ConvertToStableRepresentation(doc), StringComparer.Ordinal).ToList()).Where(g => g.Count >= 2
                    && (g.Last().Point - g.First().Point).DotProduct(direction) > 1.0 / 304.8).OrderByDescending(g => g.Count).ToList();
            if (groups.Count == 0) continue;
            var bars = groups[0];
            if (bars.Count < 2 || (bars.Last().Point - bars.First().Point).DotProduct(direction) < 1.0 / 304.8) continue;
            string key = horizontal + ":" + string.Join(",", new[] { low, bars.First().Point.DotProduct(direction), bars.Last().Point.DotProduct(direction), high }.Select(v => Math.Round(v * 304.8, 1)));
            if (!seen.Add(key)) continue;
            int lane = (horizontal ? horizontalLanes : verticalLanes).Reserve(low - frame.Origin.DotProduct(direction), high - frame.Origin.DotProduct(direction));
            XYZ origin = horizontal ? frame.Point(frame.X(bars.First().Point), minY - frame.Paper(7 + 8 * lane))
                : frame.Point(minX - frame.Paper(7 + 8 * lane), frame.Y(bars.First().Point));
            var references = new[] { faces.First().Reference,
                ResolveAxis(doc, view, bars.First(), faces.First(), origin, direction, options.DimensionTypeId, bars.Last()),
                ResolveAxis(doc, view, bars.Last(), faces.First(), origin, direction, options.DimensionTypeId, bars.First()), faces.Last().Reference };
            CreateDimension(doc, view, origin, direction, references, options.DimensionTypeId);
            result.Dimensions++;
        }
    }

    private static Reference ResolveAxis(Document doc, View view, DetailingAxis axis, DetailingFace? face, XYZ origin, XYZ direction, ElementId type, DetailingAxis? anchor = null)
    {
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
        var levels = geometry.Faces.Where(f => !f.Horizontal && Math.Abs(f.Normal.Z) > 0.999)
            .GroupBy(f => Math.Round(f.Point.Z * 304.8)).Select(g => g.OrderBy(f => frame.X(f.Point)).First()).OrderBy(f => f.Point.Z);
        double lastY = double.NegativeInfinity;
        foreach (DetailingFace face in levels)
        {
            double y = Math.Max(frame.Y(face.Point), lastY + frame.Paper(5)); lastY = y;
            XYZ origin = frame.Flatten(face.Point);
            XYZ bend = frame.Point(minX - frame.Paper(35), y), end = bend - frame.Right * frame.Paper(7);
            SpotDimension spot = doc.Create.NewSpotElevation(view, Reference.ParseFromStableRepresentation(doc, face.Reference.ConvertToStableRepresentation(doc)), origin, bend, end, face.Point, true);
            spot.ChangeTypeId(options.SpotTypeId); result.Elevations++;
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
