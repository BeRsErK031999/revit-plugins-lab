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

        const string windowKey = "truebim.launcher";
        if (ModelessWindowService.Activate(windowKey, logger))
        {
            return Result.Succeeded;
        }

        ModuleActionExternalEventHandler moduleActionHandler = new(logger);
        ExternalEvent moduleActionEvent = ExternalEvent.Create(moduleActionHandler);
        Dictionary<string, Action<System.Windows.Window>> moduleActions = new()
        {
            ["truebim.print"] = owner => moduleActionHandler.Raise(
                moduleActionEvent,
                () => TrueBimCommandActions.OpenPrint(commandData, owner, logger)),
            ["truebim.sheet-numbering"] = owner => moduleActionHandler.Raise(
                moduleActionEvent,
                () => TrueBimCommandActions.OpenSheetNumbering(commandData, owner, logger)),
            ["truebim.schedule-column-collapse"] = _ => moduleActionHandler.Raise(
                moduleActionEvent,
                () => TrueBimCommandActions.CollapseScheduleColumns(commandData, logger))
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

        window.Closed += (_, _) => moduleActionEvent.Dispose();
        ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);

        return Result.Succeeded;
    }

    private sealed class ModuleActionExternalEventHandler : IExternalEventHandler
    {
        private readonly ITrueBimLogger logger;
        private Action? pendingAction;

        public ModuleActionExternalEventHandler(ITrueBimLogger logger)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Raise(ExternalEvent externalEvent, Action action)
        {
            pendingAction = action ?? throw new ArgumentNullException(nameof(action));
            externalEvent.Raise();
        }

        public void Execute(UIApplication app)
        {
            Action? action = pendingAction;
            pendingAction = null;

            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                logger.Error("Failed to execute modeless launcher action.", exception);
                TaskDialog.Show("TrueBIM", "Не удалось выполнить действие launcher. Используйте логи для диагностики.");
            }
        }

        public string GetName()
        {
            return "TrueBIM Launcher Action";
        }
    }
}
