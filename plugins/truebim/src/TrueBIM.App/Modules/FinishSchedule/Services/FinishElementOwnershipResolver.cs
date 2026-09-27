using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

/// <summary>Контакт определяет помещение; в ведомость попадает полная площадь элемента один раз.</summary>
public sealed class FinishElementOwnershipResolver
{
    public FinishQuantityResult Resolve(
        IEnumerable<FinishOccurrence> contacts,
        IReadOnlyDictionary<long, double> fullAreas)
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
            if (!fullAreas.TryGetValue(element.Key, out double area)
                || area <= 0 || double.IsNaN(area) || double.IsInfinity(area))
            {
                warnings.Add(new FinishGeometryWarning(
                    FinishGeometryWarningCode.FullElementAreaUnavailable,
                    $"У элемента {element.Key} недоступна полная площадь Revit. Площадь контакта не используется как замена.",
                    owner.RoomId, element.Key, owner.Category));
                continue;
            }

            occurrences.Add(new FinishOccurrence(owner.RoomId, element.Key, owner.Category, area, owner.Method));
            if (candidates.Select(item => item.RoomId).Distinct().Count() > 1)
            {
                warnings.Add(new FinishGeometryWarning(
                    FinishGeometryWarningCode.MultipleRoomContacts,
                    $"Элемент {element.Key} касается нескольких помещений. Полная площадь учтена один раз в помещении "
                    + $"{owner.RoomId} с наибольшей площадью контакта. Кандидаты: "
                    + string.Join(", ", candidates.Select(item => $"{item.RoomId} ({item.AreaSquareMeters:F3} м² контакта)")) + ".",
                    owner.RoomId, element.Key, owner.Category));
            }
        }
        return new FinishQuantityResult(occurrences, warnings);
    }
}
