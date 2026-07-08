using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.BatchExport.Services;
using TrueBIM.App.Modules.BimTools.BatchExport.UI;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class BatchExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Batch Export requested without an active document.");
                TaskDialog.Show("Экспорт PDF/DWG", "Откройте документ Revit перед запуском экспорта.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            IReadOnlyList<PrintSheetInfo> sheets = new PrintSheetCollectorService().Collect(document);
            BatchExportWindow window = new(
                document,
                sheets,
                new BatchExportProfileStorage(logger),
                logger);
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Batch Export window.", exception);
            TaskDialog.Show("Экспорт PDF/DWG", "Не удалось открыть экспорт PDF/DWG. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
