using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Lintels;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Revit;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class LintelsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                LintelsModuleStatus status = LintelsModuleStatus.Create(null);
                logger.Warning("Lintels diagnostic requested without an active document.");
                TaskDialog.Show("Перемычки", status.ToDialogText());
                return Result.Succeeded;
            }

            logger.Info($"Starting read-only Lintels diagnostic. Document='{uiDocument.Document.Title}'.");
            LintelDiagnosticResult result = new LintelDiagnosticCollectorService(logger).Collect(uiDocument);
            TaskDialog dialog = new("Перемычки")
            {
                MainInstruction = result.HasCandidates
                    ? "Диагностика перемычек завершена"
                    : "Перемычки не найдены",
                MainContent = result.BuildSummary(),
                ExpandedContent = result.BuildDetails(),
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collect Lintels diagnostic.", exception);
            TaskDialog.Show(
                "Перемычки",
                "Не удалось собрать диагностику перемычек. Используйте логи для анализа ошибки.");
            return Result.Failed;
        }
    }
}
