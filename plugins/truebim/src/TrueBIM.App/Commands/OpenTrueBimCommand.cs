using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using TrueBIM.App.Modules;
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
        ModuleSettingsService moduleSettings = new(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TrueBIM",
                "2025",
                "module-settings.json"),
            logger);
        ModuleRegistry registry = ModuleRegistry.CreateForRevit2025(logger);
        logger.Info("Opening TrueBIM launcher.");
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
                        TaskDialog.Show("Sheet Numbering", "Open a Revit document before starting Sheet Numbering.");
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
                    TaskDialog.Show("Sheet Numbering", "Failed to open Sheet Numbering. Use Logs to share diagnostics.");
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
                    TaskDialog.Show("TrueBIM Logs", "Failed to open the local log file.");
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
