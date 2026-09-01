using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ScheduleRegisterSettingsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                TaskDialog.Show("Настройки ведомости спецификаций", "Откройте проект Revit.");
                return Result.Succeeded;
            }

            ScheduleRegisterTemplateInspector inspector = new(new ScheduleRegisterTemplateValidator());
            SchedulePlacementCollector collector = new();
            ScheduleRegisterSettingsStorage storage = ScheduleRegisterSettingsStorage.ForRevitVersion(
                commandData.Application.Application.VersionNumber,
                logger);
            ScheduleRegisterSettingsWindow window = new(
                storage,
                collector.CollectParameterNames(uiDocument.Document),
                inspector.Inspect(uiDocument.Document));
            RevitModalWindowService.ShowDialog(
                window,
                commandData.Application.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Schedule Register settings.", exception);
            TaskDialog.Show(
                "Настройки ведомости спецификаций",
                "Не удалось открыть настройки. Подробности сохранены в логах TrueBIM.");
            return Result.Failed;
        }
    }
}
