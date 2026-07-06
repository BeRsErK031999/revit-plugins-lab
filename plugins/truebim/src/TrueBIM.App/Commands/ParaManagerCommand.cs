using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ParaManager.Services;
using TrueBIM.App.Modules.BimTools.ParaManager.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ParaManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("ParaManager requested without an active document.");
                TaskDialog.Show("ParaManager", "Откройте документ Revit перед импортом параметров проекта.");
                return Result.Succeeded;
            }

            ProjectParameterBindingService bindingService = new(
                new SharedParameterFileService(logger),
                new CategoryResolveService(),
                logger);
            ParaManagerWindow window = new(
                commandData.Application,
                uiDocument.Document,
                new ParameterCsvImportService(),
                new ParaManagerValidationService(),
                bindingService,
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
            logger.Error("Failed to open ParaManager window.", exception);
            TaskDialog.Show("ParaManager", "Не удалось открыть ParaManager. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
