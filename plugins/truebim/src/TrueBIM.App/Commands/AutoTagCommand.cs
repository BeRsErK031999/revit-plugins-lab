using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.AutoTags.Services;
using TrueBIM.App.Modules.BimTools.AutoTags.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class AutoTagCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            const string windowKey = "truebim.auto-tags";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Auto Tags requested without an active document.");
                TaskDialog.Show("Автомарки", "Откройте документ Revit перед запуском автомарок.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            View activeView = uiDocument.ActiveView;
            if (!AutoTagCollectorService.CanUseActiveView(activeView, out string viewMessage))
            {
                logger.Warning($"Auto Tags requested for unsupported view '{activeView?.Name}': {viewMessage}");
                TaskDialog.Show("Автомарки", viewMessage);
                return Result.Succeeded;
            }

            AutoTagPlacementService placementService = new();
            AutoTagWindow window = new(
                document,
                activeView,
                new AutoTagCollectorService(placementService),
                new AutoTagProfileStorage(logger),
                placementService,
                logger);
            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Auto Tags window.", exception);
            TaskDialog.Show("Автомарки", "Не удалось открыть автомарки. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
