using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishClassificationService
{
    public FinishClassificationResult Classify(
        FinishElementCollection collection,
        FinishScheduleSettings settings)
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        List<FinishClassifiedElement> classified = [];
        List<FinishClassificationSkip> skipped = [];
        foreach (FinishElementCandidateSnapshot element in collection.Walls
                     .Concat(collection.Floors)
                     .Concat(collection.Ceilings))
        {
            if (!collection.Types.TryGetValue(element.TypeId, out FinishTypeSnapshot? type))
            {
                skipped.Add(new FinishClassificationSkip(
                    element.ElementId,
                    FinishClassificationSkipReason.MissingType));
                continue;
            }

            if (!type.HasClassificationParameter)
            {
                skipped.Add(new FinishClassificationSkip(
                    element.ElementId,
                    FinishClassificationSkipReason.MissingClassificationParameter));
                continue;
            }

            string? classificationValue = type.ClassificationValue;
            if (string.IsNullOrWhiteSpace(classificationValue))
            {
                skipped.Add(new FinishClassificationSkip(
                    element.ElementId,
                    FinishClassificationSkipReason.EmptyClassificationValue));
                continue;
            }

            bool ambiguous = false;
            FinishPreviewCategory? category = element.PhysicalCategory switch
            {
                FinishPhysicalCategory.Wall => ClassifyWall(classificationValue!, settings),
                FinishPhysicalCategory.Floor => ClassifyFloor(classificationValue!, settings, out ambiguous),
                FinishPhysicalCategory.Ceiling => ClassifyCeiling(classificationValue!, settings),
                _ => null
            };
            if (element.PhysicalCategory == FinishPhysicalCategory.Floor && ambiguous)
            {
                skipped.Add(new FinishClassificationSkip(
                    element.ElementId,
                    FinishClassificationSkipReason.AmbiguousFloorClassification));
            }
            else if (category.HasValue)
            {
                classified.Add(new FinishClassifiedElement(element, category.Value));
            }
            else
            {
                skipped.Add(new FinishClassificationSkip(
                    element.ElementId,
                    FinishClassificationSkipReason.ValueDoesNotMatch));
            }
        }

        return new FinishClassificationResult(classified, skipped);
    }

    private static FinishPreviewCategory? ClassifyWall(
        string value,
        FinishScheduleSettings settings)
    {
        return settings.Walls.IsEnabled && Matches(value, settings.Walls.ClassificationValue)
            ? FinishPreviewCategory.Walls
            : null;
    }

    private static FinishPreviewCategory? ClassifyFloor(
        string value,
        FinishScheduleSettings settings,
        out bool ambiguous)
    {
        bool isFloor = settings.Floors.IsEnabled
            && Matches(value, settings.Floors.ClassificationValue);
        bool isCeiling = settings.Ceilings.IsEnabled
            && Matches(value, settings.Ceilings.ClassificationValue);
        ambiguous = isFloor && isCeiling;
        if (ambiguous)
        {
            return null;
        }

        if (isFloor)
        {
            return FinishPreviewCategory.Floors;
        }

        return isCeiling ? FinishPreviewCategory.Ceilings : null;
    }

    private static FinishPreviewCategory? ClassifyCeiling(
        string value,
        FinishScheduleSettings settings)
    {
        return settings.Ceilings.IsEnabled && Matches(value, settings.Ceilings.ClassificationValue)
            ? FinishPreviewCategory.Ceilings
            : null;
    }

    private static bool Matches(string actual, string expected)
    {
        return string.Equals(
            actual.Trim(),
            expected.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
