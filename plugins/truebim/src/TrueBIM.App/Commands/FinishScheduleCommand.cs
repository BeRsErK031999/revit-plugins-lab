using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;
using TrueBIM.App.Modules.FinishSchedule;
using TrueBIM.App.Modules.FinishSchedule.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class FinishScheduleCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            FinishScheduleModuleStatus status = FinishScheduleModuleStatus.Create(
                uiDocument?.Document.Title);
            FinishScheduleWindow window = new(status);
            new WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            logger.Info(
                $"Finish Schedule scaffold opened. Document='{status.DocumentName}'; HasActiveDocument={status.HasActiveDocument}.");
            window.ShowDialog();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Finish Schedule scaffold.", exception);
            TaskDialog.Show(
                "Ведомость отделки",
                "Не удалось открыть окно ведомости отделки. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }
}
