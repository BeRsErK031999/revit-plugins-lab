using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using TrueBIM.App.Modules.BimTools.OpeningViews.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpeningViewsCommand : IExternalCommand
{
    private const string DialogTitle = "Фасады дверей/окон";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Opening Views requested without an active document.");
                TaskDialog.Show(DialogTitle, "Откройте документ Revit перед запуском фасадов дверей/окон.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            View activeView = uiDocument.ActiveView;
            if (!OpeningViewCollectorService.CanUseActiveView(activeView, out string viewMessage)
                || activeView is not ViewPlan activePlan)
            {
                logger.Warning($"Opening Views requested for unsupported view '{activeView?.Name}': {viewMessage}");
                TaskDialog.Show(DialogTitle, viewMessage);
                return Result.Succeeded;
            }

            OpeningViewsWindow window = new(
                document,
                activePlan,
                new OpeningViewCollectorService(),
                new OpeningViewCreationService(),
                new OpeningViewProfileStorage(logger),
                logger);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Opening Views window.", exception);
            TaskDialog.Show(DialogTitle, "Не удалось открыть фасады дверей/окон. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
