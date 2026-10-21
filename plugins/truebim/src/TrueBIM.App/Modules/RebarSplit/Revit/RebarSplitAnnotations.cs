using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using TrueBIM.App.Modules.RebarSplit.Models;

namespace TrueBIM.App.Modules.RebarSplit.Revit;

/// <summary>Одна попытка на аннотацию; неподдерживаемое оформление остаётся на обработку Revit.</summary>
internal sealed class RebarSplitAnnotations
{
    private readonly Action<string>? diagnostic;

    public RebarSplitAnnotations(Action<string>? diagnostic = null) => this.diagnostic = diagnostic;

    public void Restore(Document document, IReadOnlyList<RebarSplitSet> replacements)
    {
        Dictionary<ElementId, RebarSplitSet> sets = replacements.ToDictionary(set => set.SourceId);
        List<MultiReferenceAnnotation> multi = new FilteredElementCollector(document).OfClass(typeof(MultiReferenceAnnotation))
            .Cast<MultiReferenceAnnotation>().ToList();
        HashSet<ElementId> multiDimensions = new(multi.Select(annotation => annotation.DimensionId));
        List<IndependentTag> tags = new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).Cast<IndependentTag>()
            .Where(tag => tag.MultiReferenceAnnotationId == ElementId.InvalidElementId).ToList();
        List<Dimension> dimensions = new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>()
            .Where(dimension => !multiDimensions.Contains(dimension.Id)).ToList();

        foreach (MultiReferenceAnnotation original in multi)
            Attempt(document, () => RestoreMulti(document, original, sets));
        foreach (IndependentTag original in tags)
            Attempt(document, () => RestoreTag(document, original, sets));
        foreach (Dimension original in dimensions)
            Attempt(document, () => RestoreDimension(document, original, sets));
    }

    private void Attempt(Document document, Action restore)
    {
        using SubTransaction attempt = new(document);
        attempt.Start();
        try
        {
            restore();
            attempt.Commit();
        }
        catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
        {
            // Пользователь разрешил потерю невосстановимого оформления без предупреждений плагина.
            attempt.RollBack();
            diagnostic?.Invoke("Annotation preservation: " + exception);
        }
    }

    private static void RestoreTag(Document document, IndependentTag original, IReadOnlyDictionary<ElementId, RebarSplitSet> sets)
    {
        IList<Reference> oldReferences = TagReferences(original);
        if (!oldReferences.Any(reference => sets.ContainsKey(reference.ElementId))) return;
        List<Reference> mapped = oldReferences.Select(reference => MapTagReference(document, reference, sets)).ToList();
        if (mapped.Count == 0 || original.IsMaterialTag) return;
        IndependentTag target = IndependentTag.Create(document, original.GetTypeId(), original.OwnerViewId, mapped[0],
            original.HasLeader, original.TagOrientation, original.TagHeadPosition);
#if REVIT2022_OR_GREATER
        if (mapped.Count > 1) target.AddReferences(mapped.Skip(1).ToList());
        if (original.TagOrientation == TagOrientation.AnyModelDirection) target.RotationAngle = original.RotationAngle;
#else
        if (mapped.Count > 1) throw new InvalidOperationException("Марка с несколькими ссылками.");
#endif
        target.TagHeadPosition = original.TagHeadPosition;
        if (original.HasLeader)
        {
            target.LeaderEndCondition = original.LeaderEndCondition;
            for (int index = 0; index < mapped.Count; index++)
            {
#if REVIT2022_OR_GREATER
                if (original.LeaderEndCondition == LeaderEndCondition.Free)
                    target.SetLeaderEnd(mapped[index], original.GetLeaderEnd(oldReferences[index]));
                if (original.HasLeaderElbow(oldReferences[index]))
                    target.SetLeaderElbow(mapped[index], original.GetLeaderElbow(oldReferences[index]));
#else
                if (original.LeaderEndCondition == LeaderEndCondition.Free) target.LeaderEnd = original.LeaderEnd;
                if (original.HasElbow) target.LeaderElbow = original.LeaderElbow;
#endif
            }
        }
        CopyPresentation(document, original, target);
        document.Regenerate();
        if (target.IsOrphaned) throw new InvalidOperationException("Марка потеряла ссылку.");
        document.Delete(original.Id);
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

    private static Reference MapTagReference(Document document, Reference reference, IReadOnlyDictionary<ElementId, RebarSplitSet> sets)
    {
        if (!sets.TryGetValue(reference.ElementId, out RebarSplitSet? set)) return reference;
        int index = -1;
#if REVIT2022_OR_GREATER
        index = set.Source.GetBarIndexFromReference(reference);
#endif
        RebarSplitBar? bar = index < 0 ? set.Bars.FirstOrDefault() : set.Bars.FirstOrDefault(candidate => candidate.Position == index);
        if (bar is null) throw new InvalidOperationException("Стержень ссылки исключён из набора.");
        Rebar target = (Rebar)document.GetElement(bar.Id);
#if REVIT2023_OR_GREATER
        return target.GetSubelements().First().GetReference();
#else
        return new Reference(target);
#endif
    }

    private static void RestoreDimension(Document document, Dimension original, IReadOnlyDictionary<ElementId, RebarSplitSet> sets)
    {
        if (original is SpotDimension || original.DimensionShape != DimensionShape.Linear || original.Curve is not Line line) return;
        List<Reference> references = original.References.Cast<Reference>().ToList();
        if (!references.Any(reference => sets.ContainsKey(reference.ElementId))) return;
        View view = (View)document.GetElement(original.OwnerViewId);
        ReferenceArray mapped = new();
        foreach (Reference reference in references)
        {
            Reference targetReference = MapDimensionReference(document, reference, view, sets);
            mapped.Append(DimensionReferenceType(document, reference, targetReference));
        }
        Dimension target = document.Create.NewDimension(view, line, mapped, original.DimensionType);
        foreach (BuiltInParameter parameterId in new[] { BuiltInParameter.DIM_DISPLAY_EQ, BuiltInParameter.DIM_LEADER })
        {
            Parameter? before = original.get_Parameter(parameterId);
            Parameter? after = target.get_Parameter(parameterId);
            if (before is not null && after is not null && !after.IsReadOnly && before.AsInteger() != after.AsInteger())
                after.Set(before.AsInteger());
        }
        if (original.NumberOfSegments > 0 && original.AreSegmentsEqual != target.AreSegmentsEqual)
            target.AreSegmentsEqual = original.AreSegmentsEqual;
        document.Regenerate();
        // AreReferencesAvailable может быть false только потому, что вид закрыт.
        // Проверяем геометрическое соответствие ссылок выше и реальные значения
        // всех сегментов ниже, не отбрасывая размер из-за состояния окна вида.
        if (original.NumberOfSegments != target.NumberOfSegments)
            throw new InvalidOperationException($"Ссылки размера {original.Id} изменились: " +
                $"доступны={target.AreReferencesAvailable}, сегменты={original.NumberOfSegments}/{target.NumberOfSegments}.");
        if (original.NumberOfSegments == 0)
        {
            CheckMeasurement(original.Value, target.Value);
            target.ValueOverride = original.ValueOverride;
            target.Above = original.Above;
            target.Below = original.Below;
            target.Prefix = original.Prefix;
            target.Suffix = original.Suffix;
            if (original.IsTextPositionAdjustable() && target.IsTextPositionAdjustable() &&
                original.TextPosition is XYZ text && !text.IsAlmostEqualTo(target.TextPosition))
                target.TextPosition = text;
        }
        else
        {
            for (int index = 0; index < original.NumberOfSegments; index++)
            {
                DimensionSegment before = original.Segments.get_Item(index);
                DimensionSegment after = target.Segments.get_Item(index);
                CheckMeasurement(before.Value, after.Value);
                after.ValueOverride = before.ValueOverride;
                after.Above = before.Above;
                after.Below = before.Below;
                after.Prefix = before.Prefix;
                after.Suffix = before.Suffix;
                if (before.IsTextPositionAdjustable() && after.IsTextPositionAdjustable() &&
                    before.TextPosition is XYZ text && !text.IsAlmostEqualTo(after.TextPosition))
                    after.TextPosition = text;
            }
        }
        CopyPresentation(document, original, target);
        document.Delete(original.Id);
    }

    private static Reference MapDimensionReference(Document document, Reference reference, View view,
        IReadOnlyDictionary<ElementId, RebarSplitSet> sets)
    {
        if (!sets.TryGetValue(reference.ElementId, out RebarSplitSet? set)) return reference;
        GeometryObject expected = DimensionReferenceGeometry(document, set.Source, reference, view);
        IEnumerable<RebarSplitBar> candidates = set.Bars;
#if REVIT2022_OR_GREATER
        int position = set.Source.GetBarIndexFromReference(reference);
        if (position >= 0) candidates = candidates.Where(bar => bar.Position == position);
#endif
        foreach (RebarSplitBar bar in candidates)
        {
            Element target = document.GetElement(bar.Id);
            // Проверяем геометрию перед использованием стабильного пути: индексы граней могут измениться.
            try
            {
                string stable = reference.ConvertToStableRepresentation(document).Replace(set.Source.UniqueId, target.UniqueId);
                Reference mapped = Reference.ParseFromStableRepresentation(document, stable);
                if (SameReferenceGeometry(expected, target.GetGeometryObjectFromReference(mapped))) return mapped;
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException) { }
            Options options = new() { ComputeReferences = true, IncludeNonVisibleObjects = true, View = view };
            foreach ((GeometryObject geometry, Reference candidate) in References(target.get_Geometry(options)))
                if (SameReferenceGeometry(expected, geometry)) return candidate;
        }
        throw new InvalidOperationException("Не найдена геометрическая ссылка нового стержня.");
    }

    private static GeometryObject DimensionReferenceGeometry(Document document, Rebar source, Reference reference, View view)
    {
        GeometryObject? geometry = source.GetGeometryObjectFromReference(reference);
        if (geometry is not null) return geometry;
        // Размеры арматуры могут хранить путь линии без завершающего :LINEAR.
        // Получаем настоящую ссылку из геометрии того же вида, не угадывая номера
        // стержней или индексы геометрии в стабильном представлении.
        if (reference.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_NONE)
        {
            string linearPath = reference.ConvertToStableRepresentation(document) + ":LINEAR";
            Options options = new() { ComputeReferences = true, IncludeNonVisibleObjects = true, View = view };
            foreach ((GeometryObject candidate, Reference candidateReference) in References(source.get_Geometry(options)))
            {
                if (candidate is Curve &&
                    string.Equals(candidateReference.ConvertToStableRepresentation(document), linearPath, StringComparison.Ordinal))
                    return candidate;
            }
        }
        throw new InvalidOperationException("Геометрическая ссылка размера недоступна.");
    }

    private static Reference DimensionReferenceType(Document document, Reference source, Reference target)
    {
        // Для размеров арматуры нужен тот же вид ссылки, который хранит исходный
        // размер. Геометрию сопоставляем по полной ссылке линии, а размер получает
        // ссылку на ту же часть стержня без геометрического суффикса.
        const string linearSuffix = ":LINEAR";
        string stable = target.ConvertToStableRepresentation(document);
        if (source.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_NONE &&
            target.ElementReferenceType == ElementReferenceType.REFERENCE_TYPE_LINEAR &&
            stable.EndsWith(linearSuffix, StringComparison.Ordinal))
            return Reference.ParseFromStableRepresentation(document, stable.Substring(0, stable.Length - linearSuffix.Length));
        return target;
    }

    private static IEnumerable<(GeometryObject Geometry, Reference Reference)> References(GeometryElement? geometry)
    {
        if (geometry is null) yield break;
        foreach (GeometryObject item in geometry)
        {
            if (item is Solid solid)
            {
                foreach (Face face in solid.Faces)
                    if (face.Reference is not null) yield return (face, face.Reference);
                foreach (Edge edge in solid.Edges)
                    if (edge.Reference is not null) yield return (edge, edge.Reference);
            }
            else if (item is Curve curve && curve.Reference is not null) yield return (curve, curve.Reference);
            // GetInstanceGeometry возвращает копии без пригодных для создания размеров ссылок.
        }
    }

    private static bool SameReferenceGeometry(GeometryObject expected, GeometryObject? actual)
    {
        const double tolerance = RebarSplitPolicy.LengthToleranceFeet;
        if (expected is PlanarFace a && actual is PlanarFace b)
            return Math.Abs(Math.Abs(a.FaceNormal.DotProduct(b.FaceNormal)) - 1) < 1e-9
                && Math.Abs((b.Origin - a.Origin).DotProduct(a.FaceNormal)) <= tolerance;
        Curve? first = expected is Edge edgeA ? edgeA.AsCurve() : expected as Curve;
        Curve? second = actual is Edge edgeB ? edgeB.AsCurve() : actual as Curve;
        return first is not null && second is not null && RebarSplitGeometry.SameCurves(new[] { first }, new[] { second });
    }

    private static void RestoreMulti(Document document, MultiReferenceAnnotation original, IReadOnlyDictionary<ElementId, RebarSplitSet> sets)
    {
        if (document.GetElement(original.DimensionId) is not Dimension dimension || dimension.Curve is not Line line) return;
        List<Reference> references = dimension.References.Cast<Reference>().ToList();
        if (!references.Any(reference => sets.ContainsKey(reference.ElementId))) return;
        IndependentTag? originalTag = document.GetElement(original.TagId) as IndependentTag;
        View view = (View)document.GetElement(original.OwnerViewId);
        List<ElementId> ids = references.Select(reference => reference.ElementId).Distinct()
            .Where(id => document.GetElement(id) is Rebar)
            .SelectMany(id => sets.TryGetValue(id, out RebarSplitSet? set) ? set.Bars.Select(bar => bar.Id) : new[] { id }).ToList();
        MultiReferenceAnnotationOptions options = new((MultiReferenceAnnotationType)document.GetElement(original.GetTypeId()))
        {
            DimensionLineOrigin = line.Origin,
            DimensionLineDirection = line.Direction,
            DimensionPlaneNormal = view.ViewDirection,
            DimensionStyleType = dimension.DimensionType.StyleType,
            TagHasLeader = originalTag?.HasLeader ?? false,
            TagHeadPosition = originalTag?.TagHeadPosition ?? line.Origin
        };
        options.SetElementsToDimension(ids);
        List<Reference> additional = references.Where(reference => document.GetElement(reference.ElementId) is not Rebar).ToList();
        if (additional.Count > 0) options.SetAdditionalReferencesToDimension(additional);
        MultiReferenceAnnotation target = MultiReferenceAnnotation.Create(document, original.OwnerViewId, options);
        if (document.GetElement(target.DimensionId) is Dimension targetDimension)
            CopyPresentation(document, dimension, targetDimension);
        if (originalTag is not null && document.GetElement(target.TagId) is IndependentTag targetTag)
        {
            if (targetTag.GetTypeId() != originalTag.GetTypeId()) targetTag.ChangeTypeId(originalTag.GetTypeId());
            targetTag.TagHeadPosition = originalTag.TagHeadPosition;
            CopyPresentation(document, originalTag, targetTag);
        }
        document.Regenerate();
        document.Delete(original.Id);
    }

    private static void CopyPresentation(Document document, Element source, Element target)
    {
        if (document.GetElement(source.OwnerViewId) is View view)
        {
            view.SetElementOverrides(target.Id, view.GetElementOverrides(source.Id));
            if (source.IsHidden(view)) view.HideElements(new[] { target.Id });
        }
    }

    private static void CheckMeasurement(double? before, double? after)
    {
        if (!before.HasValue || !after.HasValue || Math.Abs(before.Value - after.Value) > RebarSplitPolicy.LengthToleranceFeet)
            throw new InvalidOperationException("Значение размера изменилось.");
    }
}
