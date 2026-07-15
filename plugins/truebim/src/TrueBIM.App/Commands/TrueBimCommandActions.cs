using System.Diagnostics;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools;
using TrueBIM.App.Modules.BimTools.UI;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Modules.Print.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Services;
using TrueBIM.App.Modules.SheetNumbering.UI;
using TrueBIM.App.Modules.VoltageDrop.UI;
using TrueBIM.App.Services.Logging;
using TrueBIM.App.UI;

namespace TrueBIM.App.Commands;

internal static class TrueBimCommandActions
{
    public static void OpenPrint(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        OpenPrintWindow(commandData, owner, logger);
    }

    public static void OpenPrintPdf(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        OpenPrintWindow(commandData, owner, logger);
    }

    public static void OpenPrintDwg(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        OpenPrintWindow(commandData, owner, logger);
    }

    private static void OpenPrintWindow(
        ExternalCommandData commandData,
        System.Windows.Window? owner,
        ITrueBimLogger logger)
    {
        try
        {
            const string windowKey = "truebim.print";
            const string windowTitle = "Печать";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return;
            }

            logger.Info($"Opening Print module: {windowTitle}.");

            Document? activeDocument = commandData.Application.ActiveUIDocument?.Document;
            if (activeDocument is null)
            {
                logger.Warning($"{windowTitle} requested without an active document.");
                TaskDialog.Show(windowTitle, "Откройте документ Revit перед запуском модуля печати.");
                return;
            }

            PrintSettingsService settingsService = new(
                PrintSettingsService.CreateSettingsPath(commandData.Application.Application.VersionNumber),
                logger);
            PrintSettings initialSettings = settingsService.Load();
            PrintPresetStorage presetStorage = new(
                PrintPresetStorage.CreateStoragePath(commandData.Application.Application.VersionNumber),
                logger);
            PrintPresetStoreState presetState = presetStorage.Load();
            PrintPreset? selectedPreset = presetState.FindPreset(presetState.LastSelectedPresetName);
            string collectionFileNameMask = selectedPreset is null
                ? initialSettings.FileNameMask
                : PrintPresetStorage.NormalizePreset(selectedPreset).Settings!.FileNameMask;
            IReadOnlyCollection<string> sheetParameterNames = new PrintFileNameTemplateService()
                .GetSheetParameterNames(collectionFileNameMask);
            Stopwatch collectionTimer = Stopwatch.StartNew();
            IReadOnlyList<PrintSheetSource> sheetSources = CollectPrintSheetSources(
                commandData.Application.Application.Documents.Cast<Document>().ToList(),
                activeDocument,
                sheetParameterNames);
            collectionTimer.Stop();
            int sheetCount = sheetSources.Sum(source => source.Sheets.Count);
            logger.Info($"{windowTitle} collected {sheetCount} sheets from {sheetSources.Count} open documents in {collectionTimer.ElapsedMilliseconds} ms. Loaded custom sheet parameters: {sheetParameterNames.Count}.");
            Stopwatch windowTimer = Stopwatch.StartNew();
            PrintWindow printWindow = new(activeDocument, sheetSources, settingsService, logger, collectionFileNameMask)
            {
                ShowInTaskbar = true
            };
            windowTimer.Stop();
            logger.Info($"{windowTitle} UI prepared in {windowTimer.ElapsedMilliseconds} ms.");
            ModelessWindowService.Show(windowKey, printWindow, commandData.Application.MainWindowHandle, logger);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Print module.", exception);
            TaskDialog.Show(
                "Печать",
                "Не удалось открыть модуль печати. Используйте логи для диагностики.");
        }
    }

    public static void OpenSheetNumbering(ExternalCommandData commandData, System.Windows.Window? owner, ITrueBimLogger logger)
    {
        try
        {
            const string windowKey = "truebim.sheet-numbering";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return;
            }

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
                ShowInTaskbar = true
            };
            ModelessWindowService.Show(windowKey, sheetNumberingWindow, commandData.Application.MainWindowHandle, logger);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Sheet Numbering window.", exception);
            TaskDialog.Show("Нумератор листов", "Не удалось открыть нумератор листов. Используйте логи для диагностики.");
        }
    }

    public static void OpenVoltageDropCalculation(IntPtr ownerHandle, ITrueBimLogger logger)
    {
        try
        {
            const string windowKey = "truebim.voltage-drop";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return;
            }

            logger.Info("Opening Voltage Drop Calculation window.");
            VoltageDropWindow window = new(logger)
            {
                ShowInTaskbar = true
            };
            ModelessWindowService.Show(windowKey, window, ownerHandle, logger);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to open Voltage Drop Calculation window.", exception);
            TaskDialog.Show("Расчет потери напряжения", "Не удалось открыть расчет потери напряжения. Используйте логи для диагностики.");
        }
    }

    public static void OpenBimToolPlaceholder(
        ExternalCommandData commandData,
        BimToolPlaceholderDefinition definition,
        System.Windows.Window? owner,
        ITrueBimLogger logger)
    {
        try
        {
            string windowKey = $"truebim.bim-tool-placeholder.{definition.SettingsKey}";
            if (ModelessWindowService.Activate(windowKey, logger))
            {
                return;
            }

            logger.Info($"Opening BIM tool scaffold: {definition.Title}.");
            string? documentTitle = commandData.Application.ActiveUIDocument?.Document?.Title;
            BimToolPlaceholderWindow window = new(definition, documentTitle, logger)
            {
                ShowInTaskbar = true
            };
            ModelessWindowService.Show(windowKey, window, commandData.Application.MainWindowHandle, logger);
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to open BIM tool scaffold: {definition.Title}.", exception);
            TaskDialog.Show(definition.Title, "Не удалось открыть окно инструмента. Используйте логи для диагностики.");
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
        Document activeDocument,
        IReadOnlyCollection<string> sheetParameterNames)
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
            PrintSheetSourceKind sourceKind = IsLinkedDocument(document)
                ? PrintSheetSourceKind.LinkedDocument
                : PrintSheetSourceKind.OpenDocument;
            IReadOnlyList<PrintSheetInfo> sheets = ReferenceEquals(document, activeDocument)
                ? collector.Collect(document, sourceId, sourceName, sourceKind, sheetParameterNames)
                : Array.Empty<PrintSheetInfo>();
            if (sheets.Count == 0 && ReferenceEquals(document, activeDocument))
            {
                continue;
            }

            sources.Add(new PrintSheetSource(sourceId, sourceName, sourceKind, document, sheets));
        }

        return sources;
    }

    private static bool IsLinkedDocument(Document document)
    {
        try
        {
            return document.IsLinked;
        }
        catch (Exception)
        {
            return false;
        }
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
