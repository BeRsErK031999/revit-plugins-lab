using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Modules.Print.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Modules.SheetNumbering.UI;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Commands;

internal static class TrueBimCommandActions
{
    public static void OpenPrint(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        try
        {
            logger.Info("Opening Print module.");

            Document? activeDocument = commandData.Application.ActiveUIDocument?.Document;
            if (activeDocument is null)
            {
                logger.Warning("Print module requested without an active document.");
                TaskDialog.Show("Печать", "Откройте документ Revit перед запуском модуля печати.");
                return;
            }

            IReadOnlyList<PrintSheetSource> sheetSources = CollectPrintSheetSources(
                commandData.Application.Application.Documents.Cast<Document>().ToList(),
                activeDocument);
            int sheetCount = sheetSources.Sum(source => source.Sheets.Count);
            logger.Info($"Print module collected {sheetCount} sheets from {sheetSources.Count} open documents.");
            PrintSettingsService settingsService = new(
                PrintSettingsService.CreateSettingsPath(commandData.Application.Application.VersionNumber),
                logger);
            PrintWindow printWindow = new(activeDocument, sheetSources, settingsService, logger)
            {
                Owner = owner
            };
            printWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Print module.", exception);
            TaskDialog.Show("Печать", "Не удалось открыть модуль печати. Используйте логи для диагностики.");
        }
    }

    public static void OpenSheetNumbering(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        try
        {
            logger.Info("Opening Sheet Numbering window.");

            Document? activeDocument = commandData.Application.ActiveUIDocument?.Document;
            if (activeDocument is null)
            {
                logger.Warning("Sheet Numbering requested without an active document.");
                TaskDialog.Show("Нумератор листов", "Откройте документ Revit перед запуском нумератора листов.");
                return;
            }

            IReadOnlyList<SheetInfo> sheets = new SheetCollectorService().Collect(activeDocument);
            logger.Info($"Sheet Numbering collected {sheets.Count} sheets from the active document.");
            SheetNumberingWindow sheetNumberingWindow = new(
                activeDocument,
                sheets,
                new SheetNumberingPreviewWorkflow(
                    new SheetNumberPreviewService(),
                    new DuplicateSheetNumberDetector()),
                new SheetNumberApplyService(),
                logger)
            {
                Owner = owner
            };
            sheetNumberingWindow.ShowDialog();
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Sheet Numbering window.", exception);
            TaskDialog.Show("Нумератор листов", "Не удалось открыть нумератор листов. Используйте логи для диагностики.");
        }
    }

    public static void CollapseScheduleColumns(ExternalCommandData commandData, ITrueBimLogger logger)
    {
        try
        {
            logger.Info("Running Schedule Column Collapse.");

            UIDocument? activeUiDocument = commandData.Application.ActiveUIDocument;
            if (activeUiDocument is null)
            {
                logger.Warning("Schedule Column Collapse requested without an active document.");
                TaskDialog.Show("Свернуть ВРС", "Откройте документ Revit перед запуском сворачивания спецификации.");
                return;
            }

            ScheduleColumnCollapseResult result = new ScheduleColumnCollapseService(
                new ScheduleColumnVisibilityAnalyzer(),
                logger).Collapse(activeUiDocument, commandData.Application.MainWindowHandle);

            TaskDialog.Show(
                "Свернуть ВРС",
                result.Succeeded
                    ? $"Обновлена спецификация: {result.ScheduleName}\nСкрыто столбцов: {result.HiddenColumnCount}\nОставлено видимыми: {result.VisibleColumnCount}"
                    : result.Message);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collapse schedule columns.", exception);
            TaskDialog.Show("Свернуть ВРС", "Не удалось свернуть спецификацию. Используйте логи для диагностики.");
        }
    }

    private static IReadOnlyList<PrintSheetSource> CollectPrintSheetSources(
        IReadOnlyList<Document> openDocuments,
        Document activeDocument)
    {
        PrintSheetCollectorService collector = new();
        List<Document> orderedDocuments = openDocuments
            .OrderBy(document => ReferenceEquals(document, activeDocument) ? 0 : 1)
            .ThenBy(document => ResolvePrintSourceBaseName(document), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        Dictionary<string, int> sourceNameCounts = orderedDocuments
            .GroupBy(ResolvePrintSourceBaseName, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.CurrentCultureIgnoreCase);
        Dictionary<string, int> sourceNameIndexes = new(StringComparer.CurrentCultureIgnoreCase);
        List<PrintSheetSource> sources = new();

        for (int index = 0; index < orderedDocuments.Count; index++)
        {
            Document document = orderedDocuments[index];
            string sourceId = CreatePrintSourceId(document, index);
            string sourceName = CreatePrintSourceName(document, sourceNameCounts, sourceNameIndexes);
            IReadOnlyList<PrintSheetInfo> sheets = collector.Collect(document, sourceId, sourceName);
            if (sheets.Count == 0)
            {
                continue;
            }

            sources.Add(new PrintSheetSource(sourceId, sourceName, document, sheets));
        }

        return sources;
    }

    private static string CreatePrintSourceId(Document document, int index)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                return document.PathName;
            }
        }
        catch (Exception)
        {
        }

        return $"{ResolvePrintSourceBaseName(document)}:{index}";
    }

    private static string CreatePrintSourceName(
        Document document,
        IReadOnlyDictionary<string, int> sourceNameCounts,
        Dictionary<string, int> sourceNameIndexes)
    {
        string baseName = ResolvePrintSourceBaseName(document);
        if (!sourceNameCounts.TryGetValue(baseName, out int count) || count <= 1)
        {
            return baseName;
        }

        sourceNameIndexes.TryGetValue(baseName, out int index);
        index++;
        sourceNameIndexes[baseName] = index;
        return $"{baseName} ({index})";
    }

    private static string ResolvePrintSourceBaseName(Document document)
    {
        return string.IsNullOrWhiteSpace(document.Title)
            ? "Документ Revit"
            : document.Title;
    }
}
