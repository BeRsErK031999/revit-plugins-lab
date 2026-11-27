using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

/// <summary>Контакт определяет помещение; в ведомость попадает полная площадь элемента один раз.</summary>
public sealed class FinishElementOwnershipResolver
{
    public static bool DirectWallContactDecides(IReadOnlyList<FinishOccurrence> contacts,
        double unmeasuredAreaLimit, double jointArea) =>
        contacts.Count == 1 && contacts[0].Category == FinishPreviewCategory.Walls
        && contacts[0].Method == FinishQuantityMethod.RoomBoundarySubface
        && contacts[0].AreaSquareMeters > unmeasuredAreaLimit + jointArea + 1e-6;

    public FinishQuantityResult Resolve(
        IEnumerable<FinishOccurrence> contacts,
        IReadOnlyDictionary<long, double> fullAreas,
        IReadOnlyDictionary<long, long>? contourOwners = null)
    {
        List<FinishOccurrence> occurrences = [];
        List<FinishGeometryWarning> warnings = [];
        foreach (IGrouping<long, FinishOccurrence> element in contacts.GroupBy(item => item.ElementId))
        {
            FinishOccurrence[] candidates = element
                .OrderByDescending(item => item.AreaSquareMeters)
                .ThenBy(item => item.RoomId)
                .ThenBy(item => item.Category)
                .ToArray();
            FinishOccurrence owner = candidates[0];
            // The threshold describes a competing pair. It never filters a single-room wall.
            bool smallWallContact = candidates.Length == 2 && candidates[0].RoomId != candidates[1].RoomId
                && candidates.All(c => c.Category == FinishPreviewCategory.Walls && c.Method != FinishQuantityMethod.ConnectedWallContour)
                && IsDominantPair(candidates[0], candidates[1]);
            bool contourResolved = !smallWallContact && contourOwners is not null
                && contourOwners.TryGetValue(element.Key, out long contourRoomId)
                && candidates.Any(candidate => candidate.RoomId == contourRoomId);
            if (contourResolved)
                owner = candidates.First(candidate => candidate.RoomId == contourOwners![element.Key]);
            if (!fullAreas.TryGetValue(element.Key, out double area)
                || area <= 0 || double.IsNaN(area) || double.IsInfinity(area))
            {
                warnings.Add(new FinishGeometryWarning(
                    FinishGeometryWarningCode.FullElementAreaUnavailable,
                    $"У элемента {element.Key} недоступна полная площадь Revit. Площадь контакта не используется как замена.",
                    owner.RoomId, element.Key, owner.Category)
                { CompetingContacts = candidates });
                continue;
            }

            occurrences.Add(new FinishOccurrence(owner.RoomId, element.Key, owner.Category, area,
                contourResolved ? FinishQuantityMethod.ConnectedWallContour : owner.Method));
            if (candidates.Select(item => item.RoomId).Distinct().Count() > 1)
            {
                warnings.Add(new FinishGeometryWarning(
                    FinishGeometryWarningCode.MultipleRoomContacts,
                    $"Элемент {element.Key} касается нескольких помещений. Полная площадь учтена один раз в помещении "
                    + $"{owner.RoomId} " + (contourResolved
                        ? "по соседним участкам единого контура отделки: разница контактов не превышает площадь торца стены. "
                        : smallWallContact ? "по правилу малого касания стены: второе пятно меньше 0,1 м², основное не менее чем в 10 раз больше. "
                        : "с наибольшей площадью контакта. ") + "Кандидаты: "
                    + string.Join(", ", candidates.Select(item => $"{item.RoomId} ({item.AreaSquareMeters:F3} м² контакта; {item.Method})")) + ".",
                    owner.RoomId, element.Key, owner.Category)
                { CompetingContacts = candidates, SmallContactResolved = smallWallContact });
            }
        }
        return new FinishQuantityResult(occurrences, warnings);
    }

    public FinishQuantityResult ResolveFloorShares(IEnumerable<FinishOccurrence> measuredShares,
        IReadOnlyDictionary<long, long> roomPhases)
    {
        List<FinishOccurrence> occurrences = [];
        List<FinishGeometryWarning> warnings = [];
        foreach (var floor in measuredShares.GroupBy(share => share.ElementId))
        {
            var shares = floor.OrderByDescending(share => share.AreaSquareMeters).ToArray();
            if (shares.Length != 2 || !IsDominantPair(shares[0], shares[1])
                || roomPhases[shares[0].RoomId] != roomPhases[shares[1].RoomId])
            {
                occurrences.AddRange(shares);
                continue;
            }

            var owner = shares[0];
            var neighbour = shares[1];
            // Sum only the two measured XY patches. The rest of the slab stays outside the schedule.
            double assignedArea = owner.AreaSquareMeters + neighbour.AreaSquareMeters;
            occurrences.Add(new FinishOccurrence(owner.RoomId, floor.Key, FinishPreviewCategory.Floors,
                assignedArea, FinishQuantityMethod.FloorRoomSurface));
            warnings.Add(new FinishGeometryWarning(FinishGeometryWarningCode.MultipleRoomContacts,
                $"Пол {floor.Key}: малое касание разрешено автоматически. Проекции в помещениях {owner.RoomId} "
                + $"({owner.AreaSquareMeters:G6} м²) и {neighbour.RoomId} ({neighbour.AreaSquareMeters:G6} м²) "
                + $"учтены в помещении {owner.RoomId}, всего {assignedArea:G6} м².",
                owner.RoomId, floor.Key, FinishPreviewCategory.Floors)
            { CompetingContacts = shares, SmallContactResolved = true });
        }
        return new FinishQuantityResult(occurrences, warnings);
    }

    private static bool IsDominantPair(FinishOccurrence larger, FinishOccurrence smaller) =>
        larger.RoomId != smaller.RoomId && smaller.AreaSquareMeters < 0.1 && larger.AreaSquareMeters >= 10 * smaller.AreaSquareMeters;
}
