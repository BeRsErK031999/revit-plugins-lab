using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleImportContextService
{
    public ScheduleImportContext Create(UIDocument uiDocument)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));

        Document document = uiDocument.Document;
        View activeView = document.ActiveView;
        List<string> warnings = [];
        bool canUseDrafting = DraftingTableService.IsDraftingCompatible(activeView);
        bool canUseBimSchedule = activeView is ViewSchedule;
        IReadOnlyList<ScheduleTarget> scheduleTargets = CollectScheduleTargets(document, activeView.Id, warnings);
        IReadOnlyList<string> availableBimScheduleParameterNames = canUseBimSchedule
            ? CollectBimScheduleParameterNames(document, (ViewSchedule)activeView, warnings)
            : Array.Empty<string>();

        if (!canUseDrafting)
        {
            warnings.Add("Активный вид не подходит для прямого размещения DetailCurve/TextNote. При создании будет предложен новый чертёжный вид.");
        }

        if (scheduleTargets.Count == 0)
        {
            warnings.Add("В документе не найдено доступных спецификаций Revit. Создайте пустую спецификацию, затем снова откройте импорт.");
        }
        else
        {
            warnings.Add($"Доступно спецификаций Revit для импорта: {scheduleTargets.Count}.");
        }

        return new ScheduleImportContext(
            string.IsNullOrWhiteSpace(document.Title) ? "Документ Revit" : document.Title,
            string.IsNullOrWhiteSpace(activeView.Name) ? "Активный вид" : activeView.Name,
            activeView.ViewType.ToString(),
            RevitElementIds.GetValue(activeView.Id),
            canUseDrafting,
            canUseBimSchedule,
            availableBimScheduleParameterNames,
            warnings,
            scheduleTargets);
    }

    private static IReadOnlyList<ScheduleTarget> CollectScheduleTargets(
        Document document,
        ElementId activeViewId,
        List<string> warnings)
    {
        try
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .Where(schedule => !schedule.IsTemplate)
                .Where(schedule => !schedule.IsTitleblockRevisionSchedule)
                .Where(schedule => !schedule.IsInternalKeynoteSchedule)
                .Select(schedule => new ScheduleTarget(
                    RevitElementIds.GetValue(schedule.Id),
                    string.IsNullOrWhiteSpace(schedule.Name) ? $"Спецификация {schedule.Id}" : schedule.Name,
                    schedule.Id == activeViewId))
                .OrderByDescending(schedule => schedule.IsActive)
                .ThenBy(schedule => schedule.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception exception)
        {
            warnings.Add($"Не удалось прочитать список спецификаций Revit: {exception.Message}");
            return Array.Empty<ScheduleTarget>();
        }
    }

    private static IReadOnlyList<string> CollectBimScheduleParameterNames(
        Document document,
        ViewSchedule schedule,
        List<string> warnings)
    {
        try
        {
            ScheduleDefinition definition = schedule.Definition;
            List<string> names = [];
            foreach (SchedulableField field in definition.GetSchedulableFields())
            {
                string name = field.GetName(document);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name.Trim());
                }
            }

            return names
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception exception)
        {
            warnings.Add($"Не удалось прочитать поля активной ViewSchedule для BIM Schedule Mode: {exception.Message}");
            return Array.Empty<string>();
        }
    }
}
