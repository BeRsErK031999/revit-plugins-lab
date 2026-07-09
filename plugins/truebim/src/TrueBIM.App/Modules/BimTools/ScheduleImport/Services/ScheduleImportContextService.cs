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

        if (!canUseDrafting)
        {
            warnings.Add("Активный вид не подходит для прямого размещения DetailCurve/TextNote. При создании будет предложен новый чертёжный вид.");
        }

        if (canUseBimSchedule)
        {
            warnings.Add("Обычная ViewSchedule не поддерживает произвольные строки из PDF. Для MVP используйте Drafting Table Mode.");
        }

        return new ScheduleImportContext(
            string.IsNullOrWhiteSpace(document.Title) ? "Документ Revit" : document.Title,
            string.IsNullOrWhiteSpace(activeView.Name) ? "Активный вид" : activeView.Name,
            activeView.ViewType.ToString(),
            RevitElementIds.GetValue(activeView.Id),
            canUseDrafting,
            canUseBimSchedule,
            warnings);
    }
}
