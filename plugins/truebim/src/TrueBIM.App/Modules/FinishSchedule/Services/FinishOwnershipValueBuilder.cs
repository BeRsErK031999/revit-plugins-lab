using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishOwnershipValueBuilder
{
    public FinishParameterTargetBuildResult Build(
        FinishScheduleSettings settings,
        IEnumerable<FinishClassifiedElement> inScopeElements,
        FinishQuantityResult quantities,
        RoomFinishSnapshotBuildResult roomSnapshots) =>
        Build(settings, inScopeElements, quantities, roomSnapshots, null);

    public FinishParameterTargetBuildResult Build(
        FinishScheduleSettings settings,
        IEnumerable<FinishClassifiedElement> inScopeElements,
        FinishQuantityResult quantities,
        RoomFinishSnapshotBuildResult roomSnapshots,
        FinishQuantityRequest? geometryRequest)
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
        List<FinishRoomAssignment> assignments = [];
        List<FinishWriteIssue> issues = [];
        var blockingWarnings = quantities.Warnings.Where(FinishGeometryWarningClassifier.AffectsScheduleValue)
            .ToLookup(warning => warning.ElementId);
        var uncertainCandidates = FindRoomWarningCandidates(blockingWarnings[null], elements, geometryRequest);
        foreach (FinishClassifiedElement element in elements)
        {
            long elementId = element.Element.ElementId;
            ParameterReference? reference = ResolveReference(settings, element.Category);
            string role = $"{GetCategoryName(element.Category)}: принадлежность";
            if (reference is null)
            {
                issues.Add(new FinishWriteIssue(
                    FinishWriteIssueCode.OutputConfigurationInvalid,
                    FinishWriteIssueSeverity.Critical,
                    $"{role}: не выбран параметр; элемент {elementId} пропущен.",
                    elementId,
                    role));
                continue;
            }

            if (uncertainCandidates.Contains(element)
                || blockingWarnings[elementId].Any(warning => warning.Category is null || warning.Category == element.Category))
            {
                issues.Add(new FinishWriteIssue(
                    FinishWriteIssueCode.UnknownOwnership,
                    FinishWriteIssueSeverity.Warning,
                    $"Элемент {elementId}: принадлежность не записана из-за неопределённой геометрии.",
                    elementId,
                    role));
                continue;
            }

            long[] roomIds = quantities.Occurrences
                .Where(occurrence => occurrence.ElementId == elementId
                    && occurrence.Category == element.Category)
                .Select(occurrence => occurrence.RoomId)
                .Distinct().ToArray();
            string[] identifiers = roomIds
                .Select(roomId => roomIdentifiers.TryGetValue(roomId, out string? identifier)
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
                isRequired: true,
                element.Category));
            assignments.Add(new FinishRoomAssignment(elementId, reference, value,
                settings.RoomIdentifier.SourceKey,
                roomIds.Length == 1 && identifiers.Length == 1 ? roomIds[0] : null));
        }

        return new FinishParameterTargetBuildResult(elements.Length, targets, issues, assignments);
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

    private static HashSet<FinishClassifiedElement> FindRoomWarningCandidates(
        IEnumerable<FinishGeometryWarning> warnings,
        IReadOnlyList<FinishClassifiedElement> elements,
        FinishQuantityRequest? geometryRequest)
    {
        var failures = warnings.Where(warning => warning.RoomId.HasValue)
            .Select(warning => (RoomId: warning.RoomId!.Value, warning.Category)).Distinct().ToArray();
        HashSet<FinishClassifiedElement> uncertain = [];
        if (failures.Length == 0)
            return uncertain;

        var rooms = geometryRequest?.Rooms.ToDictionary(room => room.ElementId);
        var search = geometryRequest is null ? null : new FinishRoomSearchBounds(geometryRequest.Rooms, geometryRequest.Elements);
        var elementsByCategory = elements.ToLookup(element => element.Category);
        foreach (var failure in failures)
        {
            FinishRoomCandidateSnapshot? room = null;
            rooms?.TryGetValue(failure.RoomId, out room);
            foreach (var category in elementsByCategory)
            {
                if (failure.Category.HasValue && failure.Category != category.Key)
                    continue;
                if (room?.Bounds is null)
                {
                    uncertain.UnionWith(category);
                    continue;
                }

                var bounds = search!.Create(room, category.Key);
                // An element without bounds cannot be ruled out. Preserve any existing assignment.
                uncertain.UnionWith(category.Where(element => element.Element.Bounds is null
                    || FinishRoomSearchBounds.Includes(room, element, bounds)));
            }
        }
        return uncertain;
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
