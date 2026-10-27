using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

/// <summary>Сохраняет оформление до замены типа группы, когда исходные элементы ещё доступны.</summary>
internal sealed class RebarSplitAnnotationSnapshot
{
    private readonly List<Entry> entries = new();
    private readonly List<string> warnings;
    private readonly Dictionary<ElementId, string> originalAnnotations = new();

    private RebarSplitAnnotationSnapshot(List<string> warnings) => this.warnings = warnings;

    internal static RebarSplitAnnotationSnapshot Capture(Document document, ISet<ElementId> affected, List<string> warnings)
    {
        RebarSplitAnnotationSnapshot result = new(warnings);
        foreach (Element annotation in new FilteredElementCollector(document).WhereElementIsNotElementType()
            .Where(element => element.ViewSpecific && element.Category?.CategoryType == CategoryType.Annotation))
            result.originalAnnotations[annotation.Id] = annotation.Category.Name;
        List<MultiReferenceAnnotation> multi = new FilteredElementCollector(document).OfClass(typeof(MultiReferenceAnnotation))
            .Cast<MultiReferenceAnnotation>().ToList();
        HashSet<ElementId> multiDimensions = new(multi.Select(item => item.DimensionId));
        foreach (MultiReferenceAnnotation annotation in multi)
        {
            if (document.GetElement(annotation.DimensionId) is not Dimension dimension) continue;
            if (!dimension.References.Cast<Reference>().Any(reference => affected.Contains(reference.ElementId))) continue;
            result.CaptureEntry(annotation.Id, () => CaptureMulti(document, annotation, dimension));
        }
        foreach (IndependentTag tag in new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
        {
            if (tag.MultiReferenceAnnotationId != ElementId.InvalidElementId) continue;
            IList<Reference> references = TagReferences(tag);
            if (!references.Any(reference => affected.Contains(reference.ElementId))) continue;
            result.CaptureEntry(tag.Id, () => CaptureTag(document, tag, references));
        }
        foreach (Dimension dimension in new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            if (multiDimensions.Contains(dimension.Id)) continue;
            List<Reference> references = dimension.References.Cast<Reference>().ToList();
            if (!references.Any(reference => affected.Contains(reference.ElementId))) continue;
            result.CaptureEntry(dimension.Id, () => CaptureDimension(document, dimension, references));
        }
        return result;
    }

    internal void RemoveCaptured(Document document)
    {
        // Смена типа группы сама пересоздаёт некоторые марки с новыми ID.
        // Удаляем сохранённые оригиналы заранее, чтобы не получить второй экземпляр при переносе.
        foreach (Entry entry in entries)
        {
            if (document.GetElement(entry.Id) is not Element source) continue;
            if (source.Pinned) source.Pinned = false;
            document.Delete(source.Id);
        }
    }

    internal void Restore(Document document, RebarSplitElementMap replacements)
    {
        foreach (Entry entry in entries)
        {
            using SubTransaction attempt = new(document);
            attempt.Start();
            try
            {
                Element target = entry.Create(document, replacements);
                document.Regenerate();
                if (target is IndependentTag tag && tag.IsOrphaned)
                    throw new InvalidOperationException("марка потеряла ссылку");
                if (document.GetElement(entry.Id) is not null) document.Delete(entry.Id);
                attempt.Commit();
                replacements[entry.Id] = new[] { target.Id };
                if (target is MultiReferenceAnnotation multi)
                {
                    if (entry.DimensionId is not null) replacements[entry.DimensionId] = new[] { multi.DimensionId };
                    if (entry.TagId is not null) replacements[entry.TagId] = new[] { multi.TagId };
                }
            }
            catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                attempt.RollBack();
                warnings.Add($"Оформление ID {entry.Id}: перенос не подтверждён — {exception.Message}");
            }
        }
    }

    internal void ReportLosses(Document document, RebarSplitElementMap replacements)
    {
        foreach (var pair in originalAnnotations)
            if (document.GetElement(pair.Key) is null && !replacements.ContainsKey(pair.Key))
                warnings.Add($"Оформление ID {pair.Key} ({pair.Value}): элемент удалён, перенос не подтверждён.");
    }

    private void CaptureEntry(ElementId id, Func<Entry> capture)
    {
        try { entries.Add(capture()); }
        catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
        {
            warnings.Add($"Оформление ID {id}: перенос не поддерживается — {exception.Message}");
        }
    }

    private static Entry CaptureTag(Document document, IndependentTag source, IList<Reference> references)
    {
        if (source.IsMaterialTag) throw new NotSupportedException("марка материала");
        ElementId type = source.GetTypeId();
        ElementId view = source.OwnerViewId;
        TagOrientation orientation = source.TagOrientation;
        XYZ head = source.TagHeadPosition;
        bool hasLeader = source.HasLeader;
        LeaderEndCondition endCondition = hasLeader ? source.LeaderEndCondition : LeaderEndCondition.Attached;
#if REVIT2022_OR_GREATER
        double rotation = 0;
        if (orientation == TagOrientation.AnyModelDirection) rotation = source.RotationAngle;
#endif
        List<ReferenceData> savedReferences = references.Select(reference => ReferenceData.Capture(document, reference, view, false)).ToList();
        List<(XYZ? End, XYZ? Elbow)> leaders = new();
        foreach (Reference reference in references)
        {
            XYZ? end = null;
            XYZ? elbow = null;
            if (hasLeader)
            {
#if REVIT2022_OR_GREATER
                if (endCondition == LeaderEndCondition.Free) end = source.GetLeaderEnd(reference);
                if (source.HasLeaderElbow(reference)) elbow = source.GetLeaderElbow(reference);
#else
                if (endCondition == LeaderEndCondition.Free) end = source.LeaderEnd;
                if (source.HasElbow) elbow = source.LeaderElbow;
#endif
            }
            leaders.Add((end, elbow));
        }
        Presentation presentation = Presentation.Capture(document, source);
        return new(source.Id, (doc, map) =>
        {
            List<Reference> mapped = savedReferences.Select(reference => reference.Resolve(doc, map, true)).ToList();
            IndependentTag target = IndependentTag.Create(doc, type, view, mapped[0], hasLeader, orientation, head);
#if REVIT2022_OR_GREATER
            if (mapped.Count > 1) target.AddReferences(mapped.Skip(1).ToList());
            if (orientation == TagOrientation.AnyModelDirection) target.RotationAngle = rotation;
#else
            if (mapped.Count > 1) throw new NotSupportedException("марка с несколькими ссылками");
#endif
            target.TagHeadPosition = head;
            if (hasLeader)
            {
                target.LeaderEndCondition = endCondition;
                for (int index = 0; index < mapped.Count; index++)
                {
#if REVIT2022_OR_GREATER
                    if (leaders[index].End is XYZ end) target.SetLeaderEnd(mapped[index], end);
                    if (leaders[index].Elbow is XYZ elbow) target.SetLeaderElbow(mapped[index], elbow);
#else
                    if (leaders[index].End is XYZ end) target.LeaderEnd = end;
                    if (leaders[index].Elbow is XYZ elbow) target.LeaderElbow = elbow;
#endif
                }
            }
            presentation.Apply(doc, target);
            return target;
        });
    }

    private static Entry CaptureDimension(Document document, Dimension source, IReadOnlyList<Reference> references)
    {
        if (source is SpotDimension || source.DimensionShape != DimensionShape.Linear || source.Curve is not Line line)
            throw new NotSupportedException("поддерживаются линейные размеры");
        Line savedLine = (Line)line.Clone();
        ElementId view = source.OwnerViewId;
        ElementId type = source.GetTypeId();
        List<ReferenceData> saved = references.Select(reference => ReferenceData.Capture(document, reference, view, true)).ToList();
        DimensionAppearance appearance = DimensionAppearance.Capture(document, source);
        return new(source.Id, (doc, map) =>
        {
            ReferenceArray mapped = new();
            foreach (ReferenceData reference in saved) mapped.Append(reference.Resolve(doc, map, false));
            Dimension target = doc.Create.NewDimension((View)doc.GetElement(view), savedLine, mapped, (DimensionType)doc.GetElement(type));
            appearance.Apply(doc, target);
            return target;
        });
    }

    private static Entry CaptureMulti(Document document, MultiReferenceAnnotation source, Dimension dimension)
    {
        if (dimension.Curve is not Line line) throw new NotSupportedException("нелинейная мультиарматурная аннотация");
        ElementId type = source.GetTypeId();
        ElementId view = source.OwnerViewId;
        XYZ origin = line.Origin;
        XYZ direction = line.Direction;
        XYZ normal = ((View)document.GetElement(view)).ViewDirection;
        DimensionStyleType style = dimension.DimensionType.StyleType;
        IndependentTag? tag = document.GetElement(source.TagId) as IndependentTag;
        ElementId tagType = tag?.GetTypeId() ?? ElementId.InvalidElementId;
        XYZ head = tag?.TagHeadPosition ?? origin;
        bool leader = tag?.HasLeader ?? false;
        Presentation? tagPresentation = tag is null ? null : Presentation.Capture(document, tag);
        DimensionAppearance appearance = DimensionAppearance.Capture(document, dimension);
        List<Reference> references = dimension.References.Cast<Reference>().ToList();
        List<ElementId> bars = references.Where(reference => document.GetElement(reference.ElementId) is Rebar)
            .Select(reference => reference.ElementId).Distinct().ToList();
        List<ReferenceData> extra = references.Where(reference => document.GetElement(reference.ElementId) is not Rebar)
            .Select(reference => ReferenceData.Capture(document, reference, view, true)).ToList();
        return new(source.Id, (doc, map) =>
        {
            MultiReferenceAnnotationOptions options = new((MultiReferenceAnnotationType)doc.GetElement(type))
            {
                DimensionLineOrigin = origin, DimensionLineDirection = direction, DimensionPlaneNormal = normal,
                DimensionStyleType = style, TagHasLeader = leader, TagHeadPosition = head
            };
            options.SetElementsToDimension(bars.SelectMany(id => map.TryGetValue(id, out IReadOnlyList<ElementId>? ids) ? ids : new[] { id }).ToList());
            if (extra.Count > 0)
            {
#if REVIT2020_OR_GREATER
                options.SetAdditionalReferencesToDimension(extra.Select(reference => reference.Resolve(doc, map, false)).ToList());
#else
                throw new NotSupportedException("дополнительные ссылки мультиарматурной аннотации в Revit 2019");
#endif
            }
            MultiReferenceAnnotation target = MultiReferenceAnnotation.Create(doc, view, options);
            appearance.Apply(doc, (Dimension)doc.GetElement(target.DimensionId));
            if (doc.GetElement(target.TagId) is IndependentTag targetTag)
            {
                if (tagType != ElementId.InvalidElementId && targetTag.GetTypeId() != tagType) targetTag.ChangeTypeId(tagType);
                targetTag.TagHeadPosition = head;
                tagPresentation?.Apply(doc, targetTag);
            }
            return target;
        }, dimension.Id, tag?.Id);
    }

    private static IList<Reference> TagReferences(IndependentTag tag)
    {
#if REVIT2022_OR_GREATER
        return tag.GetTaggedReferences();
#else
        Element? element = tag.Document.GetElement(tag.TaggedLocalElementId);
        return element is null ? new List<Reference>() : new List<Reference> { new(element) };
#endif
    }

    private sealed record Entry(ElementId Id, Func<Document, IReadOnlyDictionary<ElementId, IReadOnlyList<ElementId>>, Element> Create,
        ElementId? DimensionId = null, ElementId? TagId = null);

    private sealed record Presentation(ElementId ViewId, OverrideGraphicSettings Overrides, bool Hidden, bool Pinned)
    {
        internal static Presentation Capture(Document document, Element source)
        {
            View view = (View)document.GetElement(source.OwnerViewId);
            return new(source.OwnerViewId, view.GetElementOverrides(source.Id), source.IsHidden(view), source.Pinned);
        }
        internal void Apply(Document document, Element target)
        {
            View view = (View)document.GetElement(ViewId);
            view.SetElementOverrides(target.Id, Overrides);
            if (Hidden) view.HideElements(new[] { target.Id });
            target.Pinned = Pinned;
        }
    }

    private sealed record Segment(double? Value, string? Override, string? Above, string? Below, string? Prefix, string? Suffix, XYZ? Position)
    {
        internal static Segment Capture(Dimension source) => new(source.Value, source.ValueOverride, source.Above, source.Below,
            source.Prefix, source.Suffix, source.IsTextPositionAdjustable() ? source.TextPosition : null);
        internal static Segment Capture(DimensionSegment source) => new(source.Value, source.ValueOverride, source.Above, source.Below,
            source.Prefix, source.Suffix, source.IsTextPositionAdjustable() ? source.TextPosition : null);
        internal void Apply(Dimension target)
        {
            Check(target.Value);
            target.ValueOverride = Override; target.Above = Above; target.Below = Below; target.Prefix = Prefix; target.Suffix = Suffix;
            if (Position is not null && target.IsTextPositionAdjustable()) target.TextPosition = Position;
        }
        internal void Apply(DimensionSegment target)
        {
            Check(target.Value);
            target.ValueOverride = Override; target.Above = Above; target.Below = Below; target.Prefix = Prefix; target.Suffix = Suffix;
            if (Position is not null && target.IsTextPositionAdjustable()) target.TextPosition = Position;
        }
        private void Check(double? value)
        {
            if (!Value.HasValue || !value.HasValue || Math.Abs(Value.Value - value.Value) > RebarSplitPolicy.LengthToleranceFeet)
                throw new InvalidOperationException("измеренное значение изменилось");
        }
    }

    private sealed record DimensionAppearance(Presentation Presentation, bool Equal, int? DisplayEqual, int? Leader, IReadOnlyList<Segment> Segments, bool Segmented)
    {
        internal static DimensionAppearance Capture(Document document, Dimension source) => new(Presentation.Capture(document, source),
            source.NumberOfSegments > 0 && source.AreSegmentsEqual, source.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ)?.AsInteger(),
            source.get_Parameter(BuiltInParameter.DIM_LEADER)?.AsInteger(), source.NumberOfSegments > 0
                ? source.Segments.Cast<DimensionSegment>().Select(Segment.Capture).ToList() : new[] { Segment.Capture(source) }, source.NumberOfSegments > 0);
        internal void Apply(Document document, Dimension target)
        {
            if (DisplayEqual.HasValue && target.get_Parameter(BuiltInParameter.DIM_DISPLAY_EQ) is Parameter eq && !eq.IsReadOnly) eq.Set(DisplayEqual.Value);
            if (Leader.HasValue && target.get_Parameter(BuiltInParameter.DIM_LEADER) is Parameter leader && !leader.IsReadOnly) leader.Set(Leader.Value);
            if (Segmented && target.AreSegmentsEqual != Equal) target.AreSegmentsEqual = Equal;
            document.Regenerate();
            if ((target.NumberOfSegments > 0) != Segmented || (Segmented && target.NumberOfSegments != Segments.Count))
                throw new InvalidOperationException("количество сегментов размера изменилось");
            if (Segmented)
                for (int i = 0; i < Segments.Count; i++) Segments[i].Apply(target.Segments.get_Item(i));
            else Segments[0].Apply(target);
            Presentation.Apply(document, target);
        }
    }

    private sealed record ReferenceData(ElementId Id, string UniqueId, string Stable, ElementReferenceType Kind,
        ElementId View, int BarPosition, Curve? Curve, XYZ? PlaneOrigin, XYZ? PlaneNormal)
    {
        internal static ReferenceData Capture(Document document, Reference reference, ElementId viewId, bool geometry)
        {
            Element source = document.GetElement(reference.ElementId);
            string stable = reference.ConvertToStableRepresentation(document);
            int position = -1;
#if REVIT2022_OR_GREATER
            if (source is Rebar rebar) position = rebar.GetBarIndexFromReference(reference);
#endif
            GeometryObject? shape = null;
            if (geometry)
            {
                shape = source.GetGeometryObjectFromReference(reference);
                if (shape is null && reference.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_NONE)
                    shape = References(source.get_Geometry(new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, View = (View)document.GetElement(viewId) }))
                        .Where(pair => pair.Shape is Curve && pair.Reference.ConvertToStableRepresentation(document) == stable + ":LINEAR")
                        .Select(pair => pair.Shape).FirstOrDefault();
            }
            Curve? curve = shape is Edge edge ? edge.AsCurve() : shape as Curve;
            PlanarFace? plane = shape as PlanarFace;
            return new(source.Id, source.UniqueId, stable, reference.ElementReferenceType, viewId, position, curve?.Clone(), plane?.Origin, plane?.FaceNormal);
        }

        internal Reference Resolve(Document document, IReadOnlyDictionary<ElementId, IReadOnlyList<ElementId>> replacements, bool tag)
        {
            if (!replacements.TryGetValue(Id, out IReadOnlyList<ElementId>? ids)) return Reference.ParseFromStableRepresentation(document, Stable);
            IEnumerable<ElementId> candidates = ids;
            if (tag)
            {
                ElementId targetId = ids.FirstOrDefault() ?? throw new InvalidOperationException($"Элемент ссылки {Id} удалён без замены.");
                if (BarPosition >= 0 && replacements is RebarSplitElementMap map && map.BarPositions.TryGetValue(Id, out var positions))
                {
                    if (!positions.TryGetValue(BarPosition, out ElementId? positionId))
                        throw new InvalidOperationException("стержень ссылки исключён из набора");
                    targetId = positionId;
                }
                Element target = document.GetElement(targetId);
                if (target is Rebar set && set.Quantity > 1)
                    return Reference.ParseFromStableRepresentation(document, Stable.Replace(UniqueId, target.UniqueId));
#if REVIT2023_OR_GREATER
                if (target is Rebar rebar) return rebar.GetSubelements().First().GetReference();
#endif
                return new Reference(target);
            }
            foreach (ElementId id in candidates)
            {
                Element target = document.GetElement(id);
                try
                {
                    Reference mapped = Reference.ParseFromStableRepresentation(document, Stable.Replace(UniqueId, target.UniqueId));
                    if (Matches(target.GetGeometryObjectFromReference(mapped))) return mapped;
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { }
                foreach (var pair in References(target.get_Geometry(new Options { ComputeReferences = true, IncludeNonVisibleObjects = true, View = (View)document.GetElement(View) })))
                    if (Matches(pair.Shape)) return RestoreKind(document, pair.Reference);
            }
            throw new InvalidOperationException($"не найдена соответствующая геометрическая ссылка элемента {Id}");
        }

        private Reference RestoreKind(Document document, Reference target)
        {
            string stable = target.ConvertToStableRepresentation(document);
            if (Kind == ElementReferenceType.REFERENCE_TYPE_NONE && target.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_LINEAR
                && stable.EndsWith(":LINEAR", StringComparison.Ordinal))
                return Reference.ParseFromStableRepresentation(document, stable.Substring(0, stable.Length - ":LINEAR".Length));
            return target;
        }

        private bool Matches(GeometryObject? shape)
        {
            if (PlaneOrigin is not null && PlaneNormal is not null && shape is PlanarFace plane)
                return Math.Abs(Math.Abs(PlaneNormal.DotProduct(plane.FaceNormal)) - 1) < 1e-9
                    && Math.Abs((plane.Origin - PlaneOrigin).DotProduct(PlaneNormal)) <= RebarSplitPolicy.LengthToleranceFeet;
            Curve? curve = shape is Edge edge ? edge.AsCurve() : shape as Curve;
            return Curve is not null && curve is not null && RebarSplitGeometry.SameCurves(new[] { Curve }, new[] { curve });
        }
    }

    private static IEnumerable<(GeometryObject Shape, Reference Reference)> References(GeometryElement? geometry)
    {
        if (geometry is null) yield break;
        foreach (GeometryObject item in geometry)
        {
            if (item is Solid solid)
            {
                foreach (Face face in solid.Faces) if (face.Reference is not null) yield return (face, face.Reference);
                foreach (Edge edge in solid.Edges) if (edge.Reference is not null) yield return (edge, edge.Reference);
            }
            else if (item is Curve curve && curve.Reference is not null) yield return (curve, curve.Reference);
        }
    }
}
