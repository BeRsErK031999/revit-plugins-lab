using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class AutoMepDimensionsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Auto MEP Dimensions requested without an active document.");
                TaskDialog.Show("Авторазмеры MEP", "Откройте документ Revit перед запуском авторазмеров MEP.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            View activeView = uiDocument.ActiveView;
            if (!MepDimensionCollectorService.CanUseActiveView(activeView, out string viewMessage))
            {
                logger.Warning($"Auto MEP Dimensions requested for unsupported view '{activeView?.Name}': {viewMessage}");
                TaskDialog.Show("Авторазмеры MEP", viewMessage);
                return Result.Succeeded;
            }

            MepDimensionWindow window = new(
                document,
                activeView,
                new MepDimensionCollectorService(),
                new MepDimensionCreationService(),
                new MepDimensionProfileStorage(logger),
                logger);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Auto MEP Dimensions window.", exception);
            TaskDialog.Show("Авторазмеры MEP", "Не удалось открыть авторазмеры MEP. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
