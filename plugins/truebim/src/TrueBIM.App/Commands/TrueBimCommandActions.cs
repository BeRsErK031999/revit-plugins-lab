using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Modules.SheetNumbering.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

internal static class TrueBimCommandActions
{
    public static void OpenSheetNumbering(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        try
        {
            logger.Info("Opening Sheet Numbering window.");

            Document? activeDocument = commandData.Application.ActiveUIDocument?.Document;
            if (activeDocument is null)
            {
                logger.Warning("Sheet Numbering requested without an active document.");
                TaskDialog.Show("Нумератор листов", "Откройте документ Revit перед запуском нумератора листов.");
                return;
            }

            IReadOnlyList<SheetInfo> sheets = new SheetCollectorService().Collect(activeDocument);
            logger.Info($"Sheet Numbering collected {sheets.Count} sheets from the active document.");
            SheetNumberingWindow sheetNumberingWindow = new(
                activeDocument,
                sheets,
                new SheetNumberingPreviewWorkflow(
                    new SheetNumberPreviewService(),
                    new DuplicateSheetNumberDetector()),
                new SheetNumberApplyService(),
                logger)
            {
                Owner = owner
            };
            sheetNumberingWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Sheet Numbering window.", exception);
            TaskDialog.Show("Нумератор листов", "Не удалось открыть нумератор листов. Используйте логи для диагностики.");
        }
    }

    public static void CollapseScheduleColumns(ExternalCommandData commandData, ITrueBimLogger logger)
    {
        try
        {
            logger.Info("Running Schedule Column Collapse.");

            UIDocument? activeUiDocument = commandData.Application.ActiveUIDocument;
            if (activeUiDocument is null)
            {
                logger.Warning("Schedule Column Collapse requested without an active document.");
                TaskDialog.Show("Свернуть ВРС", "Откройте документ Revit перед запуском сворачивания спецификации.");
                return;
            }

            ScheduleColumnCollapseResult result = new ScheduleColumnCollapseService(
                new ScheduleColumnVisibilityAnalyzer(),
                logger).Collapse(activeUiDocument, commandData.Application.MainWindowHandle);

            TaskDialog.Show(
                "Свернуть ВРС",
                result.Succeeded
                    ? $"Обновлена спецификация: {result.ScheduleName}\nСкрыто столбцов: {result.HiddenColumnCount}\nОставлено видимыми: {result.VisibleColumnCount}"
                    : result.Message);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collapse schedule columns.", exception);
            TaskDialog.Show("Свернуть ВРС", "Не удалось свернуть спецификацию. Используйте логи для диагностики.");
        }
    }
}
