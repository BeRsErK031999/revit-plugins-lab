using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.Worksets.Services;
using TrueBIM.App.Modules.BimTools.Worksets.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CreateWorksetsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            const string windowKey = "truebim.create-worksets";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Create Worksets requested without an active document.");
                TaskDialog.Show("Рабочие наборы", "Откройте документ Revit перед созданием рабочих наборов.");
                return Result.Succeeded;
            }

            CreateWorksetsWindow window = new(
                uiDocument.Document,
                new WorksetCsvReader(),
                new WorksetValidationService(),
                new WorksharingService(logger),
                new WorksetCreationService(logger),
                logger);
            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Create Worksets window.", exception);
            TaskDialog.Show("Рабочие наборы", "Не удалось открыть инструмент рабочих наборов. Используйте логи для диагностики.");
            return Result.Failed;
        }

        return Result.Succeeded;
    }
}
