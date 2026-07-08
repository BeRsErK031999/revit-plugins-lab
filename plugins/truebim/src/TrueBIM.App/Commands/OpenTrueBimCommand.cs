using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.IO;
using TrueBIM.App.Modules;
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
            ["truebim.print"] = owner => TrueBimCommandActions.OpenPrint(commandData, owner, logger),
            ["truebim.sheet-numbering"] = owner => TrueBimCommandActions.OpenSheetNumbering(commandData, owner, logger),
            ["truebim.schedule-column-collapse"] = _ => TrueBimCommandActions.CollapseScheduleColumns(commandData, logger)
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

        window.ShowDialog();

        return Result.Succeeded;
    }
}
