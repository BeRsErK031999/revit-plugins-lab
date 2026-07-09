using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;
using TrueBIM.App.Modules.BimTools.CopyParameters.Services;
using TrueBIM.App.Modules.BimTools.CopyParameters.UI;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;
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
            const string windowKey = "truebim.copy-parameters";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

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

            CopyParametersApplyHandler handler = new(
                uiDocument,
                sourceElement,
                selectionService,
                copyService,
                logger);
            ExternalEvent externalEvent = ExternalEvent.Create(handler);

            CopyParametersWindow? window = null;
            window = new CopyParametersWindow(
                ParameterCopyService.BuildElementLabel(sourceElement),
                parameters,
                selectedParameters =>
                {
                    handler.SetSelectedParameters(selectedParameters);
                    externalEvent.Raise();
                });

            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
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

    private sealed class CopyParametersApplyHandler : IExternalEventHandler
    {
        private readonly UIDocument uiDocument;
        private readonly ElementId sourceElementId;
        private readonly ElementSelectionService selectionService;
        private readonly ParameterCopyService copyService;
        private readonly ITrueBimLogger logger;
        private IReadOnlyList<CopyParameterRow> selectedParameters = Array.Empty<CopyParameterRow>();

        public CopyParametersApplyHandler(
            UIDocument uiDocument,
            Element sourceElement,
            ElementSelectionService selectionService,
            ParameterCopyService copyService,
            ITrueBimLogger logger)
        {
            this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
            sourceElementId = sourceElement?.Id ?? throw new ArgumentNullException(nameof(sourceElement));
            this.selectionService = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
            this.copyService = copyService ?? throw new ArgumentNullException(nameof(copyService));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void SetSelectedParameters(IReadOnlyList<CopyParameterRow> selectedParameters)
        {
            this.selectedParameters = selectedParameters is null
                ? Array.Empty<CopyParameterRow>()
                : selectedParameters.ToList();
        }

        public void Execute(UIApplication app)
        {
            try
            {
                Document document = uiDocument.Document;
                Element? sourceElement = document.GetElement(sourceElementId);
                if (sourceElement is null)
                {
                    logger.Warning($"Copy Parameters source element '{RevitElementIds.GetValue(sourceElementId)}' was not found.");
                    TaskDialog.Show("Копирование параметров", "Исходный элемент больше не найден в документе.");
                    return;
                }

                IReadOnlyList<Element> targetElements = selectionService.PickTargetElements(uiDocument, sourceElement);
                if (targetElements.Count == 0)
                {
                    logger.Info("Copy Parameters target selection returned no elements.");
                    return;
                }

                ParameterCopyResult result = copyService.Copy(
                    document,
                    sourceElement,
                    selectedParameters,
                    targetElements);

                TaskDialog.Show("Копирование параметров", result.ToDialogText());
            }
            catch (RevitOperationCanceledException)
            {
                logger.Info("Copy Parameters cancelled by user selection.");
            }
            catch (Exception exception)
            {
                logger.Error("Failed to copy parameters.", exception);
                TaskDialog.Show("Копирование параметров", "Не удалось скопировать параметры. Используйте логи для диагностики.");
            }
        }

        public string GetName()
        {
            return "TrueBIM Copy Parameters";
        }
    }
}
