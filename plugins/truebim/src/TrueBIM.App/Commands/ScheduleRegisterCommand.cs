using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.UI;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class ScheduleRegisterCommand : IExternalCommand
{
    private const string DialogTitle = "Ведомость спецификаций";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        FileTrueBimLogger logger = new(new TrueBimLogPaths());
        try
        {
            UIDocument? uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument is null)
            {
                TaskDialog.Show(DialogTitle, "Откройте проект Revit перед запуском команды.");
                return Result.Succeeded;
            }

            Document document = uiDocument.Document;
            ElementId[] selectedElementIds = uiDocument.Selection.GetElementIds().ToArray();
            ScheduleRegisterSettingsStorage settingsStorage = ScheduleRegisterSettingsStorage.ForRevitVersion(
                commandData.Application.Application.VersionNumber,
                logger);
            ScheduleRegisterTemplateInspector inspector = new(new ScheduleRegisterTemplateValidator());
            ScheduleRegisterTemplateInspection inspection = inspector.Inspect(document);
            SchedulePlacementCollector collector = new();
            IReadOnlyList<ScheduleRegisterSheetOption> sheetOptions = new ScheduleRegisterSheetCatalogService()
                .Collect(document, selectedElementIds, uiDocument.ActiveView.Id);

            logger.Info(
                $"Schedule Register window opening. Document='{document.Title}'; "
                + $"Sheets={sheetOptions.Count}; Preselected={sheetOptions.Count(sheet => sheet.IsSelected)}; "
                + $"TemplateExists={inspection.Exists}; TemplateValid={inspection.IsValid}; "
                + $"TemplateCanRepairLocally={inspection.CanRepairLocally}.");

            ScheduleRegisterWindow window = new(
                settingsStorage,
                collector.CollectParameterNames(document),
                inspection,
                sheetOptions);
            if (RevitModalWindowService.ShowDialog(
                    window,
                    commandData.Application.MainWindowHandle) != true)
            {
                logger.Info("Schedule Register canceled in the sheet-selection window.");
                return Result.Cancelled;
            }

            ElementId[] selectedSheetIds = window.SelectedSheetIds
                .Select(RevitElementIds.Create)
                .Where(id => document.GetElement(id) is ViewSheet)
                .ToArray();
            if (selectedSheetIds.Length == 0)
            {
                logger.Warning("Schedule Register sheet-selection window returned no valid sheets.");
                ShowNoSheetsError();
                return Result.Succeeded;
            }

            ScheduleRegisterSettings settings = settingsStorage.Load();
            IReadOnlyList<string> settingsIssues = ScheduleRegisterSettingsStorage.Validate(settings);
            if (settingsIssues.Count > 0)
            {
                ShowSettingsError(settingsIssues);
                return Result.Succeeded;
            }

            inspection = inspector.Inspect(document);
            PrepareTemplate(
                commandData.Application.Application,
                uiDocument,
                document,
                selectedSheetIds,
                settings,
                inspection,
                inspector,
                logger);
            inspection = inspector.Inspect(document);
            if (!inspection.IsValid)
            {
                throw new InvalidOperationException(
                    "Подготовленный шаблон не прошёл контрольную проверку: "
                    + string.Join(" ", inspection.Validation.Issues));
            }

            ViewSchedule template = inspector.FindTemplate(document)
                                    ?? throw new InvalidOperationException("Проверенный шаблон не найден перед копированием.");
            ScheduleRegisterCollectionResult collection = collector.Collect(
                document,
                selectedSheetIds,
                settings);
            ScheduleRegisterAggregationResult aggregation = new ScheduleRegisterAggregationService()
                .Aggregate(collection.Placements);
            logger.Info(
                $"Schedule Register collected placements. Sheets={selectedSheetIds.Length}; "
                + $"Placements={collection.Placements.Count}; Excluded={collection.ExcludedByFilterCount}; "
                + $"Rows={aggregation.Rows.Count}; "
                + $"Warnings={collection.Warnings.Count + aggregation.Warnings.Count}.");
            if (aggregation.Rows.Count == 0)
            {
                ShowEmptyResult(selectedSheetIds.Length, collection.ExcludedByFilterCount);
                return Result.Succeeded;
            }

            string[] warnings = collection.Warnings
                .Concat(aggregation.Warnings)
                .Distinct()
                .ToArray();
            ScheduleRegisterCreationResult result = new ScheduleRegisterWriter(inspector, logger)
                .Create(document, template, aggregation.Rows, warnings);
            ViewSchedule? createdSchedule = document.GetElement(
                RevitElementIds.Create(result.ScheduleId)) as ViewSchedule;
            if (createdSchedule is not null)
            {
                uiDocument.ActiveView = createdSchedule;
            }

            logger.Info(
                $"Schedule Register completed. ScheduleId={result.ScheduleId}; "
                + $"Name='{result.ScheduleName}'; Rows={result.RowCount}; Warnings={result.Warnings.Count}.");
            ShowCompletion(result, selectedSheetIds.Length, collection.ExcludedByFilterCount);
            return Result.Succeeded;
        }
        catch (OperationCanceledException)
        {
            logger.Info("Schedule Register canceled by the user.");
            return Result.Cancelled;
        }
        catch (Exception exception)
        {
            logger.Error("Failed to create Schedule Register.", exception);
            message = exception.Message;
            TaskDialog dialog = new(DialogTitle)
            {
                TitleAutoPrefix = false,
                MainInstruction = "Не удалось создать ведомость спецификаций.",
                MainContent = "Изменения отменены. Подробности сохранены в логах TrueBIM.",
                ExpandedContent = exception.Message,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return Result.Failed;
        }
    }

    private static void PrepareTemplate(
        Autodesk.Revit.ApplicationServices.Application application,
        UIDocument uiDocument,
        Document document,
        IReadOnlyList<ElementId> selectedSheetIds,
        ScheduleRegisterSettings settings,
        ScheduleRegisterTemplateInspection inspection,
        ScheduleRegisterTemplateInspector inspector,
        ITrueBimLogger logger)
    {
        if (inspection.IsValid)
        {
            return;
        }

        MoveAwayFromTemplateView(uiDocument, document, selectedSheetIds, inspection.ScheduleId);
        if (inspection.CanRepairLocally)
        {
            ViewSchedule damagedTemplate = inspector.FindTemplate(document)
                                           ?? throw new InvalidOperationException(
                                               "Шаблон для локального восстановления не найден.");
            new ScheduleRegisterTemplateRepairService(inspector, logger)
                .Repair(document, damagedTemplate);
            return;
        }

        if (!ScheduleRegisterSettingsStorage.IsUsableTemplateProjectPath(settings.TemplateProjectPath))
        {
            throw new InvalidOperationException(
                "Структура шаблонной спецификации повреждена. "
                + "Выберите эталонный файл .rte или .rvt в настройках ведомости.");
        }

        new ScheduleRegisterTemplateRestoreService(inspector, logger)
            .Restore(application, document, settings.TemplateProjectPath);
    }

    private static void MoveAwayFromTemplateView(
        UIDocument uiDocument,
        Document document,
        IReadOnlyList<ElementId> selectedSheetIds,
        long? templateId)
    {
        if (!templateId.HasValue
            || RevitElementIds.GetValue(uiDocument.ActiveView.Id) != templateId.Value)
        {
            return;
        }

        ViewSheet? safeView = selectedSheetIds
            .Select(document.GetElement)
            .OfType<ViewSheet>()
            .FirstOrDefault();
        if (safeView is not null)
        {
            uiDocument.ActiveView = safeView;
        }
    }

    private static void ShowNoSheetsError()
    {
        TaskDialog dialog = new(DialogTitle)
        {
            TitleAutoPrefix = false,
            MainInstruction = "Не выбраны листы.",
            MainContent = "Снова откройте команду и отметьте один или несколько листов в окне выбора.",
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.Show();
    }

    private static void ShowSettingsError(IReadOnlyList<string> issues)
    {
        TaskDialog dialog = new(DialogTitle)
        {
            TitleAutoPrefix = false,
            MainInstruction = "Настройки фильтра заполнены не полностью.",
            MainContent = "Исправьте настройки или отключите фильтрацию.",
            ExpandedContent = string.Join(Environment.NewLine, issues.Select(issue => $"• {issue}")),
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.Show();
    }

    private static void ShowEmptyResult(int sheetCount, int excludedCount)
    {
        TaskDialog dialog = new(DialogTitle)
        {
            TitleAutoPrefix = false,
            MainInstruction = "На выбранных листах нет спецификаций для ведомости.",
            MainContent = $"Проверено листов: {sheetCount}. Исключено фильтром: {excludedCount}. Проект не изменён.",
            CommonButtons = TaskDialogCommonButtons.Close
        };
        dialog.Show();
    }

    private static void ShowCompletion(
        ScheduleRegisterCreationResult result,
        int sheetCount,
        int excludedCount)
    {
        TaskDialog dialog = new(DialogTitle)
        {
            TitleAutoPrefix = false,
            MainInstruction = $"Создана «{result.ScheduleName}».",
            MainContent = $"Обработано листов: {sheetCount}. Строк в ведомости: {result.RowCount}. Исключено фильтром: {excludedCount}.",
            CommonButtons = TaskDialogCommonButtons.Close
        };
        if (result.Warnings.Count > 0)
        {
            dialog.MainContent += Environment.NewLine
                                  + $"Предупреждений: {result.Warnings.Count}. Операция выполнена полностью.";
            dialog.ExpandedContent = string.Join(
                Environment.NewLine,
                result.Warnings.Select(warning => $"• {warning}"));
        }

        dialog.Show();
    }
}
