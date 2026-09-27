using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

/// <summary>Пересоздаёт аннотации до удаления исходного экземпляра; любая потеря требует отката.</summary>
internal sealed class FamilyReplacementAnnotationService
{
    private const double PositionTolerance = 0.1 / 304.8;
    private readonly ElementId? preferredTagTypeId;

    public FamilyReplacementAnnotationService(ElementId? preferredTagTypeId = null)
    {
        this.preferredTagTypeId = preferredTagTypeId;
    }

    public FamilyReplacementAnnotationSnapshot Capture(Document document, FamilyInstance source)
    {
        var snapshot = new FamilyReplacementAnnotationSnapshot(source.Id);
        HashSet<ElementId> nestedIds = CollectNestedIds(document, source);
        HashSet<ElementId> globallyLabeledDimensions = new(new FilteredElementCollector(document)
            .OfClass(typeof(GlobalParameter)).Cast<GlobalParameter>()
            .SelectMany(parameter => parameter.GetLabeledDimensions()));
        HashSet<ElementId> multiReferenceDimensions = new(new FilteredElementCollector(document)
            .OfClass(typeof(MultiReferenceAnnotation)).Cast<MultiReferenceAnnotation>()
            .Select(annotation => annotation.DimensionId));
        foreach (Dimension dimension in new FilteredElementCollector(document).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            List<Reference> references = dimension.References.Cast<Reference>().ToList();
            if (references.Any(reference => nestedIds.Contains(reference.ElementId)))
            {
                throw CannotPreserve(dimension, "размер привязан к вложенному экземпляру семейства; перенос такой привязки требует ручной обработки");
            }

            if (!references.Any(reference => reference.ElementId == source.Id))
            {
                continue;
            }

            EnsureUngrouped(dimension, "размер");
            if (dimension is SpotDimension || dimension.DimensionShape != DimensionShape.Linear
                || dimension.Curve is not Line || dimension.View is null
                || multiReferenceDimensions.Contains(dimension.Id))
            {
                throw CannotPreserve(dimension, "поддерживаются отдельные линейные размеры на видах");
            }

            bool hasSegments = dimension.NumberOfSegments > 0;
            if ((!hasSegments && dimension.IsLocked)
                || (dimension.NumberOfSegments > 1 && dimension.AreSegmentsEqual)
                || (hasSegments && dimension.Segments.Cast<DimensionSegment>().Any(segment => segment.IsLocked)))
            {
                throw CannotPreserve(dimension, "размер содержит замок или условие равенства EQ");
            }

            Parameter? label = dimension.get_Parameter(BuiltInParameter.DIM_LABEL);
            if (globallyLabeledDimensions.Contains(dimension.Id)
                || (label?.StorageType == StorageType.ElementId && label.AsElementId() != ElementId.InvalidElementId))
            {
                throw CannotPreserve(dimension, "размер связан с управляющим параметром");
            }

            if (!dimension.IsValid || !dimension.AreReferencesAvailable)
            {
                throw CannotPreserve(dimension, "ссылки размера недоступны; откройте его вид и повторите замену");
            }

            snapshot.Dimensions.Add(new FamilyReplacementDimensionAnnotation(dimension, ReadMeasurements(dimension)));
        }

        // Марки могут пережить удаление хозяина осиротевшими, поэтому GetDependentElements недостаточно.
        foreach (IndependentTag tag in new FilteredElementCollector(document).OfClass(typeof(IndependentTag)).Cast<IndependentTag>())
        {
            IList<Reference> references = GetTagReferences(tag);
            if (references.Any(reference => nestedIds.Contains(reference.ElementId)))
            {
                throw CannotPreserve(tag, "марка привязана к вложенному экземпляру семейства; перенос такой привязки требует ручной обработки");
            }

            if (!references.Any(reference => reference.ElementId == source.Id))
            {
                continue;
            }

            EnsureUngrouped(tag, "марка");
            if (references.Count != 1 || tag.IsMaterialTag
                || tag.MultiReferenceAnnotationId != ElementId.InvalidElementId
                || references[0].ElementReferenceType != ElementReferenceType.REFERENCE_TYPE_NONE)
            {
                throw CannotPreserve(tag, "марка ссылается на материал, геометрию или несколько элементов; требуется ручная замена");
            }

            snapshot.Tags.Add(new FamilyReplacementTagAnnotation(tag, references[0], tag.TagText, tag.TagHeadPosition));
        }

        return snapshot;
    }

    public IReadOnlyCollection<ElementId> Restore(
        Document document,
        FamilyInstance source,
        FamilyInstance target,
        FamilyReplacementAnnotationSnapshot snapshot)
    {
        if (source.Id != snapshot.SourceId)
        {
            throw new InvalidOperationException("Снимок аннотаций относится к другому экземпляру.");
        }

        var removableIds = new List<ElementId>();
        foreach (FamilyReplacementDimensionAnnotation captured in snapshot.Dimensions)
        {
            try
            {
                Dimension original = captured.Original;
                var references = new ReferenceArray();
                foreach (Reference reference in original.References)
                {
                    references.Append(reference.ElementId == source.Id ? MapReference(source, target, reference) : reference);
                }

                Dimension restored = document.Create.NewDimension(original.View, (Line)original.Curve, references, original.DimensionType)
                    ?? throw CannotPreserve(original, "Revit не создал размер с новыми ссылками");
                document.Regenerate();
                ValidateDimension(restored, captured.Measurements);
                CopyDimensionPresentation(original, restored);
                CopyPresentation(document, original, restored);
                captured.ReplacementId = restored.Id;
                removableIds.Add(original.Id);
            }
            catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                throw new InvalidOperationException($"Не удалось сохранить размер {captured.Original.Id}: {exception.Message}", exception);
            }
        }

        foreach (FamilyReplacementTagAnnotation captured in snapshot.Tags)
        {
            try
            {
                IndependentTag original = captured.Original;
                Reference targetReference = new(target);
                IndependentTag restored = CreateTag(document, source, target, original, targetReference);
                restored.TagHeadPosition = captured.HeadPosition;
#if REVIT2022_OR_GREATER
                if (original.TagOrientation == TagOrientation.AnyModelDirection)
                {
                    restored.RotationAngle = original.RotationAngle;
                }
#endif
                if (original.HasLeader)
                {
                    restored.LeaderEndCondition = original.LeaderEndCondition;
                    if (original.LeaderEndCondition == LeaderEndCondition.Free)
                    {
                        SetTagLeaderEnd(restored, targetReference, GetTagLeaderEnd(original, captured.Reference));
                    }

                    if (HasTagLeaderElbow(original, captured.Reference))
                    {
                        SetTagLeaderElbow(restored, targetReference, GetTagLeaderElbow(original, captured.Reference));
                    }
                }

                CopyPresentation(document, original, restored);
                document.Regenerate();
                ValidateTag(restored, captured);
                captured.ReplacementId = restored.Id;
                removableIds.Add(original.Id);
            }
            catch (Exception exception) when (exception is not Autodesk.Revit.Exceptions.RegenerationFailedException)
            {
                throw new InvalidOperationException($"Не удалось сохранить марку {captured.Original.Id}: {exception.Message}", exception);
            }
        }

        return removableIds;
    }

    /// <summary>Вызывается после удаления оригиналов и Document.Regenerate(), до фиксации транзакции.</summary>
    public void ValidateRestored(Document document, FamilyReplacementAnnotationSnapshot snapshot)
    {
        foreach (FamilyReplacementDimensionAnnotation captured in snapshot.Dimensions)
        {
            if (document.GetElement(captured.ReplacementId) is not Dimension dimension)
            {
                throw new InvalidOperationException("После удаления исходного экземпляра исчез пересозданный размер.");
            }

            ValidateDimension(dimension, captured.Measurements);
        }

        foreach (FamilyReplacementTagAnnotation captured in snapshot.Tags)
        {
            if (document.GetElement(captured.ReplacementId) is not IndependentTag tag)
            {
                throw new InvalidOperationException("После удаления исходного экземпляра исчезла пересозданная марка.");
            }

            ValidateTag(tag, captured);
        }
    }

    private static Reference MapReference(FamilyInstance source, FamilyInstance target, Reference reference)
    {
        FamilyInstanceReferenceType type = source.GetReferenceType(reference);
        if (type is FamilyInstanceReferenceType.CenterLeftRight
            or FamilyInstanceReferenceType.CenterFrontBack or FamilyInstanceReferenceType.CenterElevation)
        {
            IList<Reference>? candidates = target.GetReferences(type);
            if (candidates?.Count == 1)
            {
                return candidates[0];
            }
        }

        string name = source.GetReferenceName(reference);
        if (!string.IsNullOrWhiteSpace(name) && target.GetReferenceByName(name) is Reference matching)
        {
            return matching;
        }

        throw new InvalidOperationException("В новом семействе отсутствует однозначная центральная или одноимённая опорная плоскость размера. Привязку к грани или ребру нужно перенести вручную.");
    }

    private IndependentTag CreateTag(Document document, FamilyInstance source, FamilyInstance target, IndependentTag original, Reference reference)
    {
        if (preferredTagTypeId is not null && preferredTagTypeId != ElementId.InvalidElementId)
        {
            return IndependentTag.Create(document, preferredTagTypeId, original.OwnerViewId, reference, original.HasLeader, original.TagOrientation, original.TagHeadPosition);
        }

        if (original.IsMulticategoryTag || source.Category.Id == target.Category.Id)
        {
            return IndependentTag.Create(document, original.GetTypeId(), original.OwnerViewId, reference, original.HasLeader, original.TagOrientation, original.TagHeadPosition);
        }

        IndependentTag automatic;
        try
        {
            automatic = IndependentTag.Create(document, original.OwnerViewId, reference, original.HasLeader, TagMode.TM_ADDBY_CATEGORY, original.TagOrientation, original.TagHeadPosition);
        }
        catch (Autodesk.Revit.Exceptions.InvalidOperationException exception)
        {
            throw new InvalidOperationException("Загрузите и выберите совместимый тип марки для новой категории.", exception);
        }

        var validTypes = automatic.GetValidTypes().Where(id => document.GetElement(id) is FamilySymbol).ToList();
        if (validTypes.Count != 1)
        {
            throw new InvalidOperationException($"Для новой категории найдено типов марок: {validTypes.Count}. Выберите тип марки вручную в окне замены.");
        }

        if (automatic.GetTypeId() != validTypes[0])
        {
            automatic.ChangeTypeId(validTypes[0]);
        }

        return automatic;
    }

    private static List<FamilyReplacementDimensionMeasurement> ReadMeasurements(Dimension dimension)
    {
        if (dimension.NumberOfSegments > 0)
        {
            return dimension.Segments.Cast<DimensionSegment>()
                .Select(segment => new FamilyReplacementDimensionMeasurement(segment.Value, segment.Origin)).ToList();
        }

        return new List<FamilyReplacementDimensionMeasurement> { new(dimension.Value, dimension.Origin) };
    }

    private static void ValidateDimension(Dimension dimension, IReadOnlyList<FamilyReplacementDimensionMeasurement> expected)
    {
        if (!dimension.IsValid || !dimension.AreReferencesAvailable)
        {
            throw CannotPreserve(dimension, "новые ссылки размера не разрешаются");
        }

        List<FamilyReplacementDimensionMeasurement> actual = ReadMeasurements(dimension);
        if (actual.Count != expected.Count || actual.Where((measurement, index) =>
                !measurement.Value.HasValue || !expected[index].Value.HasValue
                || Math.Abs(measurement.Value.Value - expected[index].Value!.Value) > PositionTolerance
                || measurement.Origin.DistanceTo(expected[index].Origin) > PositionTolerance).Any())
        {
            throw CannotPreserve(dimension, "положение опорных плоскостей или значение размера изменилось более чем на 0,1 мм");
        }
    }

    private static void ValidateTag(IndependentTag tag, FamilyReplacementTagAnnotation captured)
    {
        if (tag.IsOrphaned || GetTagReferences(tag).Count != 1)
        {
            throw CannotPreserve(tag, "марка потеряла ссылку на элемент");
        }

        if (!string.Equals(tag.TagText, captured.Text, StringComparison.Ordinal))
        {
            throw CannotPreserve(tag, "текст новой марки отличается; выберите совместимый тип марки и проверьте параметры семейства");
        }

        if (tag.TagHeadPosition.DistanceTo(captured.HeadPosition) > PositionTolerance)
        {
            throw CannotPreserve(tag, "не удалось сохранить положение марки");
        }
    }

    private static void CopyDimensionPresentation(Dimension original, Dimension restored)
    {
#if REVIT2022_OR_GREATER
        restored.HasLeader = original.HasLeader;
#endif
        if (original.NumberOfSegments > 0)
        {
            for (int index = 0; index < original.NumberOfSegments; index++)
            {
                DimensionSegment source = original.Segments.get_Item(index);
                DimensionSegment target = restored.Segments.get_Item(index);
                target.Above = source.Above;
                target.Below = source.Below;
                target.Prefix = source.Prefix;
                target.Suffix = source.Suffix;
                target.ValueOverride = source.ValueOverride;
                if (source.IsTextPositionAdjustable())
                {
                    target.TextPosition = source.TextPosition;
#if REVIT2022_OR_GREATER
                    if (original.HasLeader && source.LeaderEndPosition is XYZ leader)
                    {
                        target.LeaderEndPosition = leader;
                    }
#endif
                }
            }
        }
        else
        {
            restored.Above = original.Above;
            restored.Below = original.Below;
            restored.Prefix = original.Prefix;
            restored.Suffix = original.Suffix;
            restored.ValueOverride = original.ValueOverride;
            if (original.IsTextPositionAdjustable())
            {
                restored.TextPosition = original.TextPosition;
#if REVIT2022_OR_GREATER
                if (original.HasLeader)
                {
                    restored.LeaderEndPosition = original.LeaderEndPosition;
                }
#endif
            }
        }
    }

    private static void CopyPresentation(Document document, Element original, Element restored)
    {
        // Графика и скрытие задаются на виде независимо от типа аннотации.
        if (document.GetElement(original.OwnerViewId) is View view)
        {
            view.SetElementOverrides(restored.Id, view.GetElementOverrides(original.Id));
            if (original.IsHidden(view))
            {
                view.HideElements(new[] { restored.Id });
            }
        }

        CopyTextParameters(original, restored);
        restored.Pinned = original.Pinned;
        if (original.Pinned)
        {
            original.Pinned = false;
        }
    }

    private static void CopyTextParameters(Element original, Element restored)
    {
        foreach (Parameter source in original.Parameters)
        {
            if (source.IsReadOnly || source.StorageType != StorageType.String || !source.HasValue)
            {
                continue;
            }

            string? value = source.AsString();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            Parameter? target = source.IsShared ? restored.get_Parameter(source.GUID) : restored.get_Parameter(source.Definition);
            if (target is null || target.IsReadOnly || target.StorageType != StorageType.String)
            {
                throw CannotPreserve(original, $"не перенесён параметр аннотации «{source.Definition.Name}»");
            }

            if (!string.Equals(target.AsString(), value, StringComparison.Ordinal) && !target.Set(value))
            {
                throw CannotPreserve(original, $"не записан параметр аннотации «{source.Definition.Name}»");
            }
        }
    }

    private static void EnsureUngrouped(Element element, string kind)
    {
        if (element.GroupId != ElementId.InvalidElementId)
        {
            throw CannotPreserve(element, $"{kind} входит в группу; требуется ручная обработка группы");
        }
    }

    private static HashSet<ElementId> CollectNestedIds(Document document, FamilyInstance source)
    {
        var ids = new HashSet<ElementId>();
        var pending = new Stack<ElementId>(source.GetSubComponentIds());
        while (pending.Count > 0)
        {
            ElementId id = pending.Pop();
            if (!ids.Add(id) || document.GetElement(id) is not FamilyInstance nested)
            {
                continue;
            }

            foreach (ElementId child in nested.GetSubComponentIds())
            {
                pending.Push(child);
            }
        }

        return ids;
    }

    private static InvalidOperationException CannotPreserve(Element element, string reason)
    {
        return new InvalidOperationException($"Аннотация {element.Id}: {reason}.");
    }

    private static IList<Reference> GetTagReferences(IndependentTag tag)
    {
#if REVIT2022_OR_GREATER
        return tag.GetTaggedReferences();
#else
        Reference? reference = tag.GetTaggedReference();
        return reference is null ? new List<Reference>() : new List<Reference> { reference };
#endif
    }

    private static bool HasTagLeaderElbow(IndependentTag tag, Reference reference)
    {
#if REVIT2022_OR_GREATER
        return tag.HasLeaderElbow(reference);
#else
        return tag.HasElbow;
#endif
    }

    private static XYZ GetTagLeaderElbow(IndependentTag tag, Reference reference)
    {
#if REVIT2022_OR_GREATER
        return tag.GetLeaderElbow(reference);
#else
        return tag.LeaderElbow;
#endif
    }

    private static XYZ GetTagLeaderEnd(IndependentTag tag, Reference reference)
    {
#if REVIT2022_OR_GREATER
        return tag.GetLeaderEnd(reference);
#else
        return tag.LeaderEnd;
#endif
    }

    private static void SetTagLeaderElbow(IndependentTag tag, Reference reference, XYZ point)
    {
#if REVIT2022_OR_GREATER
        tag.SetLeaderElbow(reference, point);
#else
        tag.LeaderElbow = point;
#endif
    }

    private static void SetTagLeaderEnd(IndependentTag tag, Reference reference, XYZ point)
    {
#if REVIT2022_OR_GREATER
        tag.SetLeaderEnd(reference, point);
#else
        tag.LeaderEnd = point;
#endif
    }
}

internal sealed class FamilyReplacementAnnotationSnapshot
{
    public FamilyReplacementAnnotationSnapshot(ElementId sourceId) => SourceId = sourceId;

    public ElementId SourceId { get; }
    public List<FamilyReplacementDimensionAnnotation> Dimensions { get; } = new();
    public List<FamilyReplacementTagAnnotation> Tags { get; } = new();
}

internal sealed class FamilyReplacementDimensionAnnotation
{
    public FamilyReplacementDimensionAnnotation(Dimension original, IReadOnlyList<FamilyReplacementDimensionMeasurement> measurements)
    {
        Original = original;
        Measurements = measurements;
    }

    public Dimension Original { get; }
    public IReadOnlyList<FamilyReplacementDimensionMeasurement> Measurements { get; }
    public ElementId ReplacementId { get; set; } = ElementId.InvalidElementId;
}

internal sealed class FamilyReplacementDimensionMeasurement
{
    public FamilyReplacementDimensionMeasurement(double? value, XYZ origin)
    {
        Value = value;
        Origin = origin;
    }

    public double? Value { get; }
    public XYZ Origin { get; }
}

internal sealed class FamilyReplacementTagAnnotation
{
    public FamilyReplacementTagAnnotation(IndependentTag original, Reference reference, string text, XYZ headPosition)
    {
        Original = original;
        Reference = reference;
        Text = text;
        HeadPosition = headPosition;
    }

    public IndependentTag Original { get; }
    public Reference Reference { get; }
    public string Text { get; }
    public XYZ HeadPosition { get; }
    public ElementId ReplacementId { get; set; } = ElementId.InvalidElementId;
}
