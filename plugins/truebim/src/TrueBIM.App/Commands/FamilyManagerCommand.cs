using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Modules.BimTools.FamilyManager.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class FamilyManagerCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Family Manager requested without an active document.");
                TaskDialog.Show("Диспетчер семейств", "Откройте документ Revit перед запуском диспетчера семейств.");
                return Result.Succeeded;
            }

            FamilyManagerWindow window = new(
                commandData.Application,
                uiDocument,
                new FamilyManagerProfileStorage(logger),
                new FamilyLibraryScanner(),
                new FamilyLoadService(),
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
            logger.Error("Failed to open Family Manager window.", exception);
            TaskDialog.Show("Диспетчер семейств", "Не удалось открыть диспетчер семейств. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
