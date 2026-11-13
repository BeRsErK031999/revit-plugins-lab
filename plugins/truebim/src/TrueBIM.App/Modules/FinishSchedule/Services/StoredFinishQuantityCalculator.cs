using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed record StoredFinishElement(
    long ElementId,
    FinishPreviewCategory Category,
    long? LevelId,
    string? Ownership,
    double AreaSquareMeters,
    long? AssignedRoomId = null);

/// <summary>Считает полную площадь по записанной принадлежности, без поиска геометрических контактов.</summary>
public sealed class StoredFinishQuantityCalculator
{
    public FinishQuantityResult Calculate(
        IEnumerable<StoredFinishElement> elements,
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        RoomIdentifierSettings identifierSettings)
    {
        Dictionary<string, FinishRoomCandidateSnapshot[]> byIdentifier = rooms
            .GroupBy(room => RoomFinishSnapshotBuilder.ResolveIdentifier(room, identifierSettings, []),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        List<FinishOccurrence> occurrences = [];
        List<FinishGeometryWarning> warnings = [];
        foreach (StoredFinishElement element in elements.GroupBy(item => item.ElementId).Select(group => group.First()))
        {
            string identifier = FinishDescriptionNormalizer.CollapseWhitespace(element.Ownership);
            if (identifier.Length == 0 || !byIdentifier.TryGetValue(identifier, out FinishRoomCandidateSnapshot[]? matches))
            {
                warnings.Add(UnresolvedOwnership(element,
                    identifier.Length == 0
                        ? "Принадлежность помещению не заполнена."
                        : $"Помещение с идентификатором «{identifier}» не найдено. Укажите идентификатор существующего помещения."));
                continue;
            }

            // Automatic assignment retains the exact room independently of its display identifier.
            // A stale assignment cannot override a changed text parameter or a renamed/deleted room.
            FinishRoomCandidateSnapshot? assigned = element.AssignedRoomId.HasValue
                ? matches.FirstOrDefault(room => room.ElementId == element.AssignedRoomId.Value)
                : null;
            FinishRoomCandidateSnapshot[] candidates = assigned is not null
                ? [assigned]
                : matches.Length == 1
                ? matches
                : matches.Where(room => room.LevelId == element.LevelId).ToArray();
            // Equal identifiers form one logical room. Attribute the element once to a stable
            // physical member; aggregation combines all members and their different finishes.
            long roomId = (candidates.Length > 0 ? candidates : matches).Min(room => room.ElementId);
            if (element.AreaSquareMeters <= 0 || double.IsNaN(element.AreaSquareMeters) || double.IsInfinity(element.AreaSquareMeters))
            {
                warnings.Add(new FinishGeometryWarning(
                    FinishGeometryWarningCode.FullElementAreaUnavailable,
                    $"Элемент {element.ElementId}: полная системная площадь недоступна.",
                    roomId, element.ElementId, element.Category));
                continue;
            }

            occurrences.Add(new FinishOccurrence(roomId, element.ElementId, element.Category,
                element.AreaSquareMeters, FinishQuantityMethod.StoredOwnership));
        }

        return new FinishQuantityResult(occurrences, warnings);
    }

    private static FinishGeometryWarning UnresolvedOwnership(StoredFinishElement element, string issue) =>
        new(FinishGeometryWarningCode.StoredOwnershipUnresolved,
            $"Элемент {element.ElementId}: {issue} Площадь не учтена.",
            ElementId: element.ElementId, Category: element.Category)
        {
            StoredOwnershipIssue = issue
        };
}
