using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using TrueBIM.App.Modules;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Modules.SheetNumbering.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenTrueBimCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        TrueBimLogFileOpener logFileOpener = new(logger);
        string revitVersion = commandData.Application.Application.VersionNumber;
        ModuleSettingsService moduleSettings = new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TrueBIM",
                revitVersion,
                "module-settings.json"),
            logger);
        ModuleRegistry registry = ModuleRegistry.CreateForRevitVersion(revitVersion, logger);
        logger.Info("Opening TrueBIM launcher.");
        logger.Info("Revit version: " + revitVersion);
        logger.Info("Modules found: " + string.Join(", ", registry.Modules.Select(module => module.Id)));

        Dictionary<string, Action<System.Windows.Window>> moduleActions = new()
        {
            ["truebim.sheet-numbering"] = owner =>
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
            },
            ["truebim.schedule-column-collapse"] = _ =>
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
                        logger).Collapse(activeUiDocument);

                    TaskDialog.Show(
                        "Свернуть ВРС",
                        result.Succeeded
                            ? $"Создана копия: {result.CollapsedScheduleName}\nСкрыто столбцов: {result.HiddenColumnCount}\nОставлено видимыми: {result.VisibleColumnCount}"
                            : result.Message);
                }
                catch (Exception exception)
                {
                    logger.Error("Failed to collapse schedule columns.", exception);
                    TaskDialog.Show("Свернуть ВРС", "Не удалось свернуть спецификацию. Используйте логи для диагностики.");
                }
            }
        };

        ModuleLauncherWindow window = new(
            registry.Modules,
            moduleActions,
            (moduleId, isEnabled) => moduleSettings.SetEnabled(moduleId, isEnabled),
            _ =>
            {
                try
                {
                    logFileOpener.OpenLogFile();
                }
                catch (Exception exception)
                {
                    logger.Error("Failed to open local log file.", exception);
                    TaskDialog.Show("Логи TrueBIM", "Не удалось открыть локальный файл логов.");
                }
            });

        System.Windows.Interop.WindowInteropHelper helper = new(window)
        {
            Owner = commandData.Application.MainWindowHandle
        };

        window.ShowDialog();

        return Result.Succeeded;
    }
}
