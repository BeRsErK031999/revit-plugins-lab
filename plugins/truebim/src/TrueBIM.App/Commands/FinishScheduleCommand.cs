using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;
using TrueBIM.App.Modules.FinishSchedule;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Modules.FinishSchedule.UI;
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
            FinishScheduleWindow window = new(
                status,
                catalog,
                ParameterCatalogService.TargetCategories,
                levels,
                profileStorage,
                uiDocument is null
                    ? null
                    : settings => previewService.Build(uiDocument.Document, settings),
                logger);
            new WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            logger.Info(
                $"Finish Schedule settings opened. Document='{status.DocumentName}'; HasActiveDocument={status.HasActiveDocument}.");
            window.ShowDialog();
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
}
