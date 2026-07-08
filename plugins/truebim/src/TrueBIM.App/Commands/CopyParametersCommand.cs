using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;
using TrueBIM.App.Modules.BimTools.CopyParameters.Services;
using TrueBIM.App.Modules.BimTools.CopyParameters.UI;
using TrueBIM.App.Services.Logging;
using RevitOperationCanceledException = Autodesk.Revit.Exceptions.OperationCanceledException;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CopyParametersCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Copy Parameters requested without an active document.");
                TaskDialog.Show("Копирование параметров", "Откройте документ Revit перед копированием параметров.");
                return Result.Succeeded;
            }

            ElementSelectionService selectionService = new();
            ParameterCopyService copyService = new(new ParameterCompatibilityService(), logger);
            Element sourceElement = selectionService.ResolveSourceElement(uiDocument);
            IReadOnlyList<CopyParameterRow> parameters = copyService.CollectCopyableParameters(uiDocument.Document, sourceElement);
            if (parameters.Count == 0)
            {
                logger.Warning($"Copy Parameters found no copyable parameters for '{ParameterCopyService.BuildElementLabel(sourceElement)}'.");
                TaskDialog.Show("Копирование параметров", "У выбранного исходного элемента не найдено изменяемых параметров со значениями.");
                return Result.Succeeded;
            }

            CopyParametersWindow window = new(ParameterCopyService.BuildElementLabel(sourceElement), parameters);
            bool? dialogResult = window.ShowDialog();
            if (dialogResult != true)
            {
                logger.Info("Copy Parameters cancelled before target selection.");
                return Result.Cancelled;
            }

            IReadOnlyList<CopyParameterRow> selectedParameters = window.SelectedParameters;
            IReadOnlyList<Element> targetElements = selectionService.PickTargetElements(uiDocument, sourceElement);
            if (targetElements.Count == 0)
            {
                logger.Info("Copy Parameters target selection returned no elements.");
                return Result.Cancelled;
            }

            ParameterCopyResult result = copyService.Copy(
                uiDocument.Document,
                sourceElement,
                selectedParameters,
                targetElements);

            TaskDialog.Show("Копирование параметров", result.ToDialogText());
            return Result.Succeeded;
        }
        catch (RevitOperationCanceledException)
        {
            logger.Info("Copy Parameters cancelled by user selection.");
            return Result.Cancelled;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to copy parameters.", exception);
            TaskDialog.Show("Копирование параметров", "Не удалось скопировать параметры. Используйте логи для диагностики.");
            return Result.Failed;
        }

    }
}
