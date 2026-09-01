using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

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
            ElementId[] selectedSheetIds = uiDocument.Selection.GetElementIds()
                .Where(id => document.GetElement(id) is ViewSheet)
                .ToArray();
            if (selectedSheetIds.Length == 0)
            {
                ShowNoSheetsError();
                return Result.Succeeded;
            }

            ScheduleRegisterSettingsStorage settingsStorage = ScheduleRegisterSettingsStorage.ForRevitVersion(
                commandData.Application.Application.VersionNumber,
                logger);
            ScheduleRegisterSettings settings = settingsStorage.Load();
            IReadOnlyList<string> settingsIssues = ScheduleRegisterSettingsStorage.Validate(settings);
            if (settingsIssues.Count > 0)
            {
                ShowSettingsError(settingsIssues);
                return Result.Succeeded;
            }

            ScheduleRegisterTemplateInspector inspector = new(new ScheduleRegisterTemplateValidator());
            ScheduleRegisterTemplateInspection inspection = inspector.Inspect(document);
            if (!inspection.IsValid)
            {
                if (!CanRestoreTemplate(settings, inspection))
                {
                    return Result.Succeeded;
                }

                if (inspection.ScheduleId.HasValue
                    && RevitElementIds.GetValue(uiDocument.ActiveView.Id) == inspection.ScheduleId.Value)
                {
                    ViewSheet? safeView = selectedSheetIds
                        .Select(document.GetElement)
                        .OfType<ViewSheet>()
                        .FirstOrDefault();
                    if (safeView is not null)
                    {
                        uiDocument.ActiveView = safeView;
                    }
                }

                ScheduleRegisterTemplateRestoreService restoreService = new(inspector, logger);
                restoreService.Restore(
                    commandData.Application.Application,
                    document,
                    settings.TemplateProjectPath);
                inspection = inspector.Inspect(document);
                if (!inspection.IsValid)
                {
                    throw new InvalidOperationException(
                        "Восстановленный шаблон не прошёл контрольную проверку.");
                }
            }

            ViewSchedule template = inspector.FindTemplate(document)
                                    ?? throw new InvalidOperationException("Проверенный шаблон не найден перед копированием.");
            SchedulePlacementCollector collector = new();
            ScheduleRegisterCollectionResult collection = collector.Collect(
                document,
                selectedSheetIds,
                settings);
            ScheduleRegisterAggregationResult aggregation = new ScheduleRegisterAggregationService()
                .Aggregate(collection.Placements);
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

            ShowCompletion(result, selectedSheetIds.Length, collection.ExcludedByFilterCount);
            return Result.Succeeded;
        }
        catch (OperationCanceledException)
        {
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

    private static bool CanRestoreTemplate(
        ScheduleRegisterSettings settings,
        ScheduleRegisterTemplateInspection inspection)
    {
        string details = string.Join(Environment.NewLine, inspection.Validation.Issues.Select(issue => $"• {issue}"));
        if (string.IsNullOrWhiteSpace(settings.TemplateProjectPath)
            || !File.Exists(settings.TemplateProjectPath))
        {
            TaskDialog dialog = new(DialogTitle)
            {
                TitleAutoPrefix = false,
                MainInstruction = inspection.Exists
                    ? "Шаблонная спецификация повреждена."
                    : "Шаблонная спецификация отсутствует.",
                MainContent = "Откройте «Настройки ведомости» и выберите эталонный файл .rte или .rvt для восстановления.",
                ExpandedContent = details,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            dialog.Show();
            return false;
        }

        TaskDialog confirmation = new(DialogTitle)
        {
            TitleAutoPrefix = false,
            MainInstruction = inspection.Exists
                ? "Восстановить повреждённый шаблон и продолжить?"
                : "Добавить шаблон из эталонного файла и продолжить?",
            MainContent = inspection.Exists
                ? "Текущая шаблонная спецификация будет удалена и заново скопирована из выбранного эталонного файла. Ранее созданные ведомости не изменятся."
                : "Исправная шаблонная спецификация будет скопирована из выбранного эталонного файла.",
            ExpandedContent = details + Environment.NewLine + Environment.NewLine
                              + $"Источник: {settings.TemplateProjectPath}",
            CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
            DefaultButton = TaskDialogResult.No
        };
        return confirmation.Show() == TaskDialogResult.Yes;
    }

    private static void ShowNoSheetsError()
    {
        TaskDialog dialog = new(DialogTitle)
        {
            TitleAutoPrefix = false,
            MainInstruction = "Не выбраны листы.",
            MainContent = "Выберите один или несколько листов в диспетчере проекта и снова нажмите кнопку.",
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
