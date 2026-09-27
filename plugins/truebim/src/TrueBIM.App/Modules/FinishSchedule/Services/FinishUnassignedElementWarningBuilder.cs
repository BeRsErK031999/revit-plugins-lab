using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

/// <summary>Не позволяет классифицированной отделке исчезнуть из расчёта без объяснения.</summary>
public sealed class FinishUnassignedElementWarningBuilder
{
    public IReadOnlyList<FinishGeometryWarning> Build(IEnumerable<FinishClassifiedElement> elements,
        IEnumerable<FinishOccurrence> contacts, ISet<long> consideredElementIds)
    {
        HashSet<long> contactedIds = new(contacts.Select(contact => contact.ElementId));
        return elements.Where(element => !contactedIds.Contains(element.Element.ElementId))
            .GroupBy(element => element.Element.ElementId).Select(group => group.First())
            .OrderBy(element => element.Element.ElementId)
            .Select(element => new FinishGeometryWarning(FinishGeometryWarningCode.UnassignedElement,
                $"Для элемента отделки {element.Element.ElementId} не определено помещение. "
                + (consideredElementIds.Contains(element.Element.ElementId)
                    ? "Элемент проверен, но контакт с помещением не найден."
                    : "Элемент не попал в область поиска ни одного помещения.")
                + " Проверьте контур, уровень элемента и наличие помещений.",
                ElementId: element.Element.ElementId, Category: element.Category))
            .ToArray();
    }
}
