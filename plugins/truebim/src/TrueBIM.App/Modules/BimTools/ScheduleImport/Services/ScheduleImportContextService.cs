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
        IReadOnlyList<string> availableBimScheduleParameterNames = canUseBimSchedule
            ? CollectBimScheduleParameterNames(document, (ViewSchedule)activeView, warnings)
            : Array.Empty<string>();

        if (!canUseDrafting)
        {
            warnings.Add("Активный вид не подходит для прямого размещения DetailCurve/TextNote. При создании будет предложен новый чертёжный вид.");
        }

        if (canUseBimSchedule)
        {
            warnings.Add($"Активная ViewSchedule доступна для BIM Schedule Mode: найдено полей для сопоставления: {availableBimScheduleParameterNames.Count}.");
            warnings.Add("Обычная ViewSchedule не поддерживает произвольные строки из PDF. В этом срезе BIM Schedule Mode доступен как read-only предпросмотр сопоставления; создание пока выполняется через Drafting Table Mode.");
        }

        return new ScheduleImportContext(
            string.IsNullOrWhiteSpace(document.Title) ? "Документ Revit" : document.Title,
            string.IsNullOrWhiteSpace(activeView.Name) ? "Активный вид" : activeView.Name,
            activeView.ViewType.ToString(),
            RevitElementIds.GetValue(activeView.Id),
            canUseDrafting,
            canUseBimSchedule,
            availableBimScheduleParameterNames,
            warnings);
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
