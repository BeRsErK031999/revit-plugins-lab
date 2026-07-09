using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class OpenTrueBimLogsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        TrueBimLogFileOpener opener = new(logger);

        try
        {
            opener.OpenLogFile();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open local log file from ribbon.", exception);
            TaskDialog.Show("Логи TrueBIM", "Не удалось открыть локальный файл логов.");
            return Result.Failed;
        }
    }
}
