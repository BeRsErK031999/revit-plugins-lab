using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using TrueBIM.App.Modules.BimTools.ScheduleImport.UI;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ScheduleImportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());

        try
        {
            const string windowKey = "truebim.schedule-import";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return Result.Succeeded;
            }

            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                logger.Warning("Schedule Import requested without an active document.");
                TaskDialog.Show("Импорт таблиц", "Откройте документ Revit перед импортом таблиц.");
                return Result.Succeeded;
            }

            if (uiDocument.Document.IsFamilyDocument)
            {
                logger.Warning("Schedule Import requested for a family document.");
                TaskDialog.Show("Импорт таблиц", "Инструмент доступен только для проектных документов Revit.");
                return Result.Succeeded;
            }

            ScheduleImportContext context = new ScheduleImportContextService().Create(uiDocument);
            ScheduleFieldCatalogExternalEventHandler fieldCatalogHandler = new(uiDocument);
            ScheduleImportExternalEventHandler scheduleHandler = new(uiDocument, logger);
            ExternalEvent fieldCatalogEvent = ExternalEvent.Create(fieldCatalogHandler);
            ExternalEvent scheduleEvent = ExternalEvent.Create(scheduleHandler);
            ScheduleImportWindow window = new(
                context,
                ScheduleImportParserFactory.Create(logger),
                logger,
                (categoryId, onCompleted, onFailed) =>
                {
                    fieldCatalogHandler.SetRequest(categoryId, onCompleted, onFailed);
                    fieldCatalogEvent.Raise();
                },
                (request, onCompleted, onFailed) =>
                {
                    scheduleHandler.SetRequest(request, onCompleted, onFailed);
                    scheduleEvent.Raise();
                })
            {
                ShowInTaskbar = true
            };

            window.Closed += (_, _) =>
            {
                fieldCatalogEvent.Dispose();
                scheduleEvent.Dispose();
            };
            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
            return Result.Succeeded;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Schedule Import window.", exception);
            TaskDialog.Show("Импорт таблиц", "Не удалось открыть импорт таблиц. Используйте логи для диагностики.");
            return Result.Failed;
        }
    }

    private sealed class ScheduleImportExternalEventHandler : IExternalEventHandler
    {
        private readonly UIDocument uiDocument;
        private readonly ITrueBimLogger logger;
        private readonly ParametricScheduleService parametricScheduleService;
        private ScheduleImportRequest? pendingRequest;
        private Action<ScheduleImportCreationResult>? onCompleted;
        private Action<Exception>? onFailed;
        private string? approvedPreviewFingerprint;

        public ScheduleImportExternalEventHandler(UIDocument uiDocument, ITrueBimLogger logger)
        {
            this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            parametricScheduleService = new ParametricScheduleService(
                new ParsedTableValidationService(),
                new ScheduleMappingConfigurationService(),
                logger);
        }

        public void SetRequest(
            ScheduleImportRequest request,
            Action<ScheduleImportCreationResult> onCompleted,
            Action<Exception> onFailed)
        {
            pendingRequest = request ?? throw new ArgumentNullException(nameof(request));
            this.onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            this.onFailed = onFailed ?? throw new ArgumentNullException(nameof(onFailed));
        }

        public void Execute(UIApplication app)
        {
            ScheduleImportRequest? request = pendingRequest;
            Action<ScheduleImportCreationResult>? completed = onCompleted;
            Action<Exception>? failed = onFailed;
            pendingRequest = null;
            onCompleted = null;
            onFailed = null;

            if (request is null)
            {
                return;
            }

            try
            {
                if (!request.PreviewOnly
                    && !string.Equals(
                        approvedPreviewFingerprint,
                        request.ConfigurationFingerprint,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Создание заблокировано: сначала выполните актуальный «Предпросмотр Revit» для выбранных полей и условий.");
                }

                Document document = uiDocument.Document;
                ScheduleImportCreationResult result = parametricScheduleService.Execute(document, request);
                if (result.Succeeded && result.IsPreview)
                {
                    approvedPreviewFingerprint = result.ConfigurationFingerprint;
                }
                else if (result.Succeeded)
                {
                    approvedPreviewFingerprint = null;
                }

                if (ScheduleImportViewActivationPolicy.ShouldOpenCreatedSchedule(result))
                {
                    try
                    {
                        ViewSchedule? createdSchedule = document.GetElement(
                            RevitElementIds.Create(result.ScheduleId!.Value)) as ViewSchedule;
                        if (createdSchedule is null || createdSchedule.IsTemplate)
                        {
                            throw new InvalidOperationException($"Created schedule id {result.ScheduleId} is unavailable.");
                        }

                        uiDocument.ActiveView = createdSchedule;
                        result = result with { OpenedInSeparateTab = true };
                        logger.Info($"Schedule Import opened ViewSchedule '{createdSchedule.Name}' in a separate Revit view tab.");
                    }
                    catch (Exception activationException)
                    {
                        string warning = "Спецификация создана, но Revit не смог автоматически открыть её в отдельной вкладке.";
                        result = result with { Warnings = result.Warnings.Append(warning).ToList() };
                        logger.Warning($"Schedule Import could not open created schedule id {result.ScheduleId} in a separate Revit view tab: {activationException.Message}");
                    }
                }

                completed?.Invoke(result);
            }
            catch (Exception exception)
            {
                logger.Error("Schedule Import external event failed.", exception);
                failed?.Invoke(exception);
            }
        }

        public string GetName()
        {
            return "TrueBIM Schedule Import";
        }
    }

    private sealed class ScheduleFieldCatalogExternalEventHandler : IExternalEventHandler
    {
        private readonly UIDocument uiDocument;
        private readonly ScheduleFieldCatalogService catalogService = new();
        private long? pendingCategoryId;
        private Action<ScheduleFieldCatalogResult>? onCompleted;
        private Action<Exception>? onFailed;

        public ScheduleFieldCatalogExternalEventHandler(UIDocument uiDocument)
        {
            this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
        }

        public void SetRequest(
            long categoryId,
            Action<ScheduleFieldCatalogResult> onCompleted,
            Action<Exception> onFailed)
        {
            pendingCategoryId = categoryId;
            this.onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            this.onFailed = onFailed ?? throw new ArgumentNullException(nameof(onFailed));
        }

        public void Execute(UIApplication app)
        {
            long? categoryId = pendingCategoryId;
            Action<ScheduleFieldCatalogResult>? completed = onCompleted;
            Action<Exception>? failed = onFailed;
            pendingCategoryId = null;
            onCompleted = null;
            onFailed = null;
            if (categoryId is null)
            {
                return;
            }

            try
            {
                completed?.Invoke(catalogService.LoadFields(uiDocument.Document, categoryId.Value));
            }
            catch (Exception exception)
            {
                failed?.Invoke(exception);
            }
        }

        public string GetName()
        {
            return "TrueBIM Schedule Field Catalog";
        }
    }
}
