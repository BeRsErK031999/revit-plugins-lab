using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.DatumExtents.Services;
using TrueBIM.App.Modules.BimTools.DatumExtents.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class DatumExtentCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            const string windowKey = "truebim.datum-extents";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Datum Extents requested without an active document.");
                TaskDialog.Show("Оси 2D/3D", "Откройте документ Revit перед запуском управления осями.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            View activeView = uiDocument.ActiveView;
            if (!DatumExtentCollectorService.CanUseActiveView(activeView, out string viewMessage))
            {
                logger.Warning($"Datum Extents requested for unsupported view '{activeView?.Name}': {viewMessage}");
                TaskDialog.Show("Оси 2D/3D", viewMessage);
                return Result.Succeeded;
            }

            DatumExtentService datumExtentService = new();
            DatumExtentWindow window = new(
                document,
                activeView,
                datumExtentService,
                logger);
            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Datum Extents window.", exception);
            TaskDialog.Show("Оси 2D/3D", "Не удалось открыть управление осями. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}

public sealed class DatumExtentCommandAvailability : IExternalCommandAvailability
{
    public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
    {
        try
        {
            UIDocument? uiDocument = applicationData.ActiveUIDocument;
            return uiDocument is not null
                && DatumExtentCollectorService.CanUseActiveView(uiDocument.ActiveView, out _);
        }
        catch
        {
            return false;
        }
    }
}
