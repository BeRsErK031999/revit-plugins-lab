using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishOwnershipValueBuilder
{
    public FinishParameterTargetBuildResult Build(
        FinishScheduleSettings settings,
        IEnumerable<FinishClassifiedElement> inScopeElements,
        FinishQuantityResult quantities,
        RoomFinishSnapshotBuildResult roomSnapshots)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (!settings.WriteOwnership)
        {
            return new FinishParameterTargetBuildResult(0, [], []);
        }

        FinishClassifiedElement[] elements = (inScopeElements
                ?? throw new ArgumentNullException(nameof(inScopeElements)))
            .GroupBy(element => new { element.Element.ElementId, element.Category })
            .Select(group => group.First())
            .OrderBy(element => element.Element.ElementId)
            .ThenBy(element => element.Category)
            .ToArray();
        if (quantities is null)
        {
            throw new ArgumentNullException(nameof(quantities));
        }

        if (roomSnapshots is null)
        {
            throw new ArgumentNullException(nameof(roomSnapshots));
        }

        IReadOnlyDictionary<long, string> roomIdentifiers = roomSnapshots.Rooms
            .ToDictionary(room => room.RoomId, room => room.Identifier);
        List<FinishParameterTargetValue> targets = [];
        List<FinishWriteIssue> issues = [];
        foreach (FinishClassifiedElement element in elements)
        {
            long elementId = element.Element.ElementId;
            ParameterReference? reference = ResolveReference(settings, element.Category);
            string role = $"{GetCategoryName(element.Category)}: принадлежность";
            if (reference is null)
            {
                issues.Add(new FinishWriteIssue(
                    FinishWriteIssueCode.OutputConfigurationInvalid,
                    FinishWriteIssueSeverity.Warning,
                    $"{role}: не выбран параметр; элемент {elementId} пропущен.",
                    elementId,
                    role));
                continue;
            }

            if (HasUnknownOwnership(elementId, element.Category, quantities.Warnings))
            {
                issues.Add(new FinishWriteIssue(
                    FinishWriteIssueCode.UnknownOwnership,
                    FinishWriteIssueSeverity.Warning,
                    $"Элемент {elementId}: принадлежность не записана из-за неопределённой геометрии.",
                    elementId,
                    role));
                continue;
            }

            string[] identifiers = quantities.Occurrences
                .Where(occurrence => occurrence.ElementId == elementId
                    && occurrence.Category == element.Category)
                .Select(occurrence => roomIdentifiers.TryGetValue(occurrence.RoomId, out string? identifier)
                    ? identifier
                    : null)
                .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(identifier => identifier, NaturalStringComparer.Instance)
                .ToArray();
            string value = string.Join(", ", identifiers);
            if (identifiers.Length == 0)
            {
                issues.Add(new FinishWriteIssue(
                    FinishWriteIssueCode.UnassignedOwnership,
                    FinishWriteIssueSeverity.Warning,
                    $"Элемент {elementId} не связан ни с одним помещением текущей области; ownership будет очищен.",
                    elementId,
                    role));
            }

            targets.Add(new FinishParameterTargetValue(
                elementId,
                reference,
                role,
                value,
                isRequired: false,
                element.Category));
        }

        return new FinishParameterTargetBuildResult(elements.Length, targets, issues);
    }

    private static ParameterReference? ResolveReference(
        FinishScheduleSettings settings,
        FinishPreviewCategory category)
    {
        return category switch
        {
            FinishPreviewCategory.Walls => settings.Walls.OwnershipParameter,
            FinishPreviewCategory.Floors => settings.Floors.OwnershipParameter,
            FinishPreviewCategory.Ceilings => settings.Ceilings.OwnershipParameter,
            _ => null
        };
    }

    private static bool HasUnknownOwnership(
        long elementId,
        FinishPreviewCategory category,
        IEnumerable<FinishGeometryWarning> warnings)
    {
        return warnings.Any(warning =>
            (!warning.Category.HasValue || warning.Category == category)
            && (warning.ElementId == elementId
                || (!warning.ElementId.HasValue && warning.RoomId.HasValue)));
    }

    private static string GetCategoryName(FinishPreviewCategory category)
    {
        return category switch
        {
            FinishPreviewCategory.Walls => "Стены",
            FinishPreviewCategory.Floors => "Полы",
            FinishPreviewCategory.Ceilings => "Потолки",
            _ => category.ToString()
        };
    }
}
