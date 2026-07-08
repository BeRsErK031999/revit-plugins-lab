using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using TrueBIM.App.Modules.BimTools.ClashReport.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ClashReportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Clash Report requested without an active document.");
                TaskDialog.Show("Отчёт коллизий", "Откройте документ Revit перед запуском отчёта коллизий.");
                return Result.Succeeded;
            }

            ClashReportWindow window = new(
                uiDocument,
                new ClashCsvImporter(),
                new ClashElementResolver(),
                new ClashViewNavigator(logger),
                new ClashReportStorage(logger),
                logger);
            System.Windows.Interop.WindowInteropHelper helper = new(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Clash Report window.", exception);
            TaskDialog.Show("Отчёт коллизий", "Не удалось открыть отчёт коллизий. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
