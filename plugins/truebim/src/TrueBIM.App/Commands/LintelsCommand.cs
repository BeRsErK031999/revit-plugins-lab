using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Interop;
using TrueBIM.App.Modules.Lintels;
using TrueBIM.App.Modules.Lintels.Models;
using TrueBIM.App.Modules.Lintels.Revit;
using TrueBIM.App.Modules.Lintels.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class LintelsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            const string windowKey = "truebim.lintels";
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                LintelsModuleStatus status = LintelsModuleStatus.Create(null);
                logger.Warning("Lintels diagnostic requested without an active document.");
                TaskDialog.Show("Перемычки", status.ToDialogText());
                return Result.Succeeded;
            }

            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            bool hasCurrentSelection = uiDocument.Selection.GetElementIds().Count > 0;
            LintelSourceModeWindow sourceWindow = new(hasCurrentSelection);
            new WindowInteropHelper(sourceWindow)
            {
                Owner = commandData.Application.MainWindowHandle
            };
            if (sourceWindow.ShowDialog() != true)
            {
                logger.Info("Lintels source selection was cancelled. Model was not changed.");
                return Result.Cancelled;
            }

            LintelWizardSourceMode sourceMode = sourceWindow.SelectedMode;
            logger.Info($"Starting read-only Lintels selection preview. Document='{uiDocument.Document.Title}'; SourceMode={sourceMode}.");
            LintelDiagnosticCollectorService collectorService = new(logger);
            LintelDiagnosticResult result = collectorService.Collect(uiDocument, sourceMode);
            LintelsWindow window = new(uiDocument, collectorService, sourceMode, result, logger);
            ModelessWindowService.Show(
                windowKey,
                window,
                commandData.Application.MainWindowHandle,
                logger);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Lintels selection preview.", exception);
            TaskDialog.Show(
                "Перемычки",
                "Не удалось открыть окно перемычек. Используйте логи для анализа ошибки.");
            return Result.Failed;
        }
    }
}
