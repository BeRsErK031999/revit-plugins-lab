using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using TrueBIM.App.Modules.BimTools.ScheduleImport.UI;
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
            ScheduleImportExternalEventHandler handler = new(uiDocument, logger);
            ExternalEvent externalEvent = ExternalEvent.Create(handler);
            ScheduleImportWindow window = new(
                context,
                new JsonOrSamplePdfTableParser(new ScheduleTableJsonReader()),
                logger,
                (request, onCompleted, onFailed) =>
                {
                    handler.SetRequest(request, onCompleted, onFailed);
                    externalEvent.Raise();
                })
            {
                ShowInTaskbar = true
            };

            window.Closed += (_, _) => externalEvent.Dispose();
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
        private readonly DraftingTableService draftingTableService;
        private ScheduleImportRequest? pendingRequest;
        private Action<DraftingTableCreationResult>? onCompleted;
        private Action<Exception>? onFailed;

        public ScheduleImportExternalEventHandler(UIDocument uiDocument, ITrueBimLogger logger)
        {
            this.uiDocument = uiDocument ?? throw new ArgumentNullException(nameof(uiDocument));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            draftingTableService = new DraftingTableService(
                new DraftingTableLayoutService(),
                new ParsedTableValidationService(),
                logger);
        }

        public void SetRequest(
            ScheduleImportRequest request,
            Action<DraftingTableCreationResult> onCompleted,
            Action<Exception> onFailed)
        {
            pendingRequest = request ?? throw new ArgumentNullException(nameof(request));
            this.onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
            this.onFailed = onFailed ?? throw new ArgumentNullException(nameof(onFailed));
        }

        public void Execute(UIApplication app)
        {
            ScheduleImportRequest? request = pendingRequest;
            Action<DraftingTableCreationResult>? completed = onCompleted;
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
                Document document = uiDocument.Document;
                View activeView = document.ActiveView;
                DraftingTableCreationResult result = draftingTableService.CreateTable(
                    document,
                    activeView,
                    request.Table,
                    request.Options);
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
}
