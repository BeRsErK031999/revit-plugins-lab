using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;
using TrueBIM.App.Modules.FinishSchedule;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Modules.FinishSchedule.UI;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class FinishScheduleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            FinishScheduleModuleStatus status = FinishScheduleModuleStatus.Create(
                uiDocument?.Document.Title);
            ParameterCatalogService catalogService = new(logger);
            ParameterCatalog catalog = uiDocument is null
                ? new ParameterCatalog([])
                : catalogService.Collect(uiDocument.Document);
            IReadOnlyList<FinishScheduleLevelOption> levels = uiDocument is null
                ? []
                : new FinishScheduleLevelCatalogService().Collect(uiDocument.Document);
            FinishScheduleProfileStorage profileStorage = new(logger);
            FinishSchedulePreviewService previewService = new(
                new FinishElementCollector(logger),
                new FinishSchedulePreviewBuilder(
                    new RoomScopeService(),
                    new FinishClassificationService()),
                logger);
            FinishParameterChangePlanner changePlanner = new();
            FinishScheduleWriteWorkflow writeWorkflow = new(
                previewService,
                new RoomFinishWriteValueBuilder(),
                new FinishOwnershipValueBuilder(),
                new RoomFinishParameterWriter(changePlanner),
                new FinishOwnershipWriter(changePlanner),
                new FinishRoomSchedulePlanBuilder(),
                new FinishRoomScheduleBuilder(new FinishScheduleMetadataService()),
                logger);
            FinishScheduleWindow window = new(
                status,
                catalog,
                ParameterCatalogService.TargetCategories,
                levels,
                profileStorage,
                uiDocument is null
                    ? null
                    : settings => previewService.Build(uiDocument.Document, settings),
                uiDocument is null
                    ? null
                    : settings => writeWorkflow.Prepare(uiDocument.Document, settings),
                uiDocument is null
                    ? null
                    : preview => writeWorkflow.Apply(uiDocument.Document, preview),
                logger);
            new WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            logger.Info(
                $"Finish Schedule settings opened. Document='{status.DocumentName}'; HasActiveDocument={status.HasActiveDocument}.");
            window.ShowDialog();
            OpenRequestedSchedule(uiDocument, window.RequestedScheduleId, logger);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Finish Schedule settings.", exception);
            TaskDialog.Show(
                "Ведомость отделки",
                "Не удалось открыть окно ведомости отделки. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }

    private static void OpenRequestedSchedule(
        UIDocument? uiDocument,
        long? scheduleId,
        ITrueBimLogger logger)
    {
        if (uiDocument is null || !scheduleId.HasValue)
        {
            return;
        }

        ViewSchedule? schedule = uiDocument.Document.GetElement(
            RevitElementIds.Create(scheduleId.Value)) as ViewSchedule;
        if (schedule is null || schedule.IsTemplate)
        {
            logger.Warning($"Finish Schedule view {scheduleId.Value} was not found after closing settings.");
            TaskDialog.Show(
                "Ведомость отделки",
                "Спецификация больше не существует или недоступна для открытия.");
            return;
        }

        try
        {
            uiDocument.ActiveView = schedule;
            logger.Info($"Finish Schedule opened managed schedule {scheduleId.Value} ('{schedule.Name}').");
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to activate Finish Schedule view {scheduleId.Value}.", exception);
            TaskDialog.Show(
                "Ведомость отделки",
                "Спецификация сформирована, но Revit не смог сделать её активным видом.");
        }
    }
}
