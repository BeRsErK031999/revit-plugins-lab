using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed record StoredFinishElement(
    long ElementId,
    FinishPreviewCategory Category,
    long? LevelId,
    string? Ownership,
    double AreaSquareMeters);

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

            // A repeated number can be resolved on the element's level, but never by report scope or geometry.
            FinishRoomCandidateSnapshot[] candidates = matches.Length == 1
                ? matches
                : matches.Where(room => room.LevelId == element.LevelId).ToArray();
            if (candidates.Length != 1)
            {
                warnings.Add(UnresolvedOwnership(element,
                    $"Записанная принадлежность «{identifier}» неоднозначна "
                        + $"(ID помещений: {string.Join(", ", matches.Select(room => room.ElementId))})."));
                continue;
            }

            long roomId = candidates[0].ElementId;
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
