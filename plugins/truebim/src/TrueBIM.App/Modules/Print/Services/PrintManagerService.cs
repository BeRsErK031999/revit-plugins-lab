using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintManagerService
{
    public IReadOnlyList<PrintSetupOption> GetPrintSetups(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        List<PrintSetupOption> options =
        [
            new PrintSetupOption(null, "Текущая настройка каждого документа")
        ];
        options.AddRange(new FilteredElementCollector(document)
            .OfClass(typeof(PrintSetting))
            .Cast<PrintSetting>()
            .Select(setting => setting.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new PrintSetupOption(name, name)));
        return options;
    }

    public PrintDriverResult Print(
        Document document,
        IReadOnlyList<PrintDriverJobItem> items,
        string printerName,
        string? printSetupName,
        ITrueBimLogger logger,
        Action<PrintOperationProgressStep>? progress = null,
        PrintPdfExportSettings? settings = null,
        bool automaticPaper = false)
    {
        return PrintCore(document, items, printerName, printSetupName, logger, progress, settings, automaticPaper, null);
    }

    internal PrintDriverResult PrintCore(
        Document document, IReadOnlyList<PrintDriverJobItem> items, string printerName, string? printSetupName,
        ITrueBimLogger logger, Action<PrintOperationProgressStep>? progress, PrintPdfExportSettings? settings,
        bool automaticPaper, IReadOnlyDictionary<long, string>? outputPaths)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (items is null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (items.Count == 0)
        {
            return new PrintDriverResult(0, Array.Empty<PrintDriverFailure>());
        }

        if (string.IsNullOrWhiteSpace(printerName))
        {
            return FailAll(items, "Принтер не выбран.");
        }

        Autodesk.Revit.DB.PrintManager? printManager = null;
        PrintManagerSnapshot? snapshot = null;
        PrintParameters? modifiedParameters = null;
        TransactionGroup? temporarySettings = null;
        LayoutSnapshot? originalLayout = null;
        AutoSheetPrintLayout? layout = null;
        (ColorDepthType Color, RasterQualityType Quality, HiddenLineViewsType Processing)? originalParameters = null;
        try
        {
            printManager = document.PrintManager;
            snapshot = PrintManagerSnapshot.Capture(printManager);
            if (automaticPaper)
            {
                if (document.IsReadOnly || document.IsLinked)
                    return FailAll(items, "Для автоподбора бумаги откройте модель отдельно или выберите экспорт PDF.");
                temporarySettings = new TransactionGroup(document, "TrueBIM — временные настройки печати");
                temporarySettings.Start();
                originalLayout = LayoutSnapshot.Capture(printManager.PrintSetup.CurrentPrintSetting.PrintParameters);
            }
            printManager.SelectNewPrintDriver(printerName);
            if (printManager.IsVirtual == VirtualPrinterType.None)
            {
                printManager.PrintToFile = false;
            }

            var blocks = automaticPaper ? PrintSheetCollectorService.CollectTitleBlocks(document) : null;
            if (automaticPaper)
            {
                layout = new AutoSheetPrintLayout(document, printManager);
            }
            else if (!string.IsNullOrWhiteSpace(printSetupName))
            {
                PrintSetting? printSetting = FindPrintSetting(document, printSetupName!);
                if (printSetting is null)
                {
                    return FailAll(items, $"Настройка печати «{printSetupName}» отсутствует в документе «{document.Title}».");
                }

                printManager.PrintSetup.CurrentPrintSetting = printSetting;
            }

            logger.Info($"Print driver job started for document '{document.Title}'. Printer: {printerName}. Print setup: {printSetupName ?? "current document setting"}. Range: selected sheets. Sheets: {items.Count}.");
            if (!automaticPaper && settings is not null)
            {
                modifiedParameters = printManager.PrintSetup.CurrentPrintSetting.PrintParameters;
                originalParameters = (modifiedParameters.ColorDepth, modifiedParameters.RasterQuality, modifiedParameters.HiddenLineViews);
                ApplyRendering(modifiedParameters, settings);
                printManager.Apply();
            }

            int printedSheetCount = 0;
            Dictionary<long, string> largerPaper = new();
            Dictionary<long, PrintPaperSize> expectedPaper = new();
            List<PrintDriverFailure> failures = new();
            foreach (PrintDriverJobItem item in items)
            {
                string itemName = $"{item.SheetNumber} — {item.SheetName}";
                progress?.Invoke(new PrintOperationProgressStep(
                    "Печать",
                    itemName,
                    PrintOperationProgressPhase.Started));
                try
                {
                    ViewSheet? sheet = document.GetElement(RevitElementIds.Create(item.ElementId)) as ViewSheet;
                    if (sheet is null || sheet.IsPlaceholder || !sheet.CanBePrinted)
                    {
                        failures.Add(new PrintDriverFailure(item, "Лист недоступен или не поддерживает печать."));
                        continue;
                    }

                    PrintPaperMatch? paper = null;
                    if (layout is not null)
                    {
                        var size = PrintSheetCollectorService.ReadPaperSize(sheet, blocks![item.ElementId])
                            ?? throw new InvalidOperationException("Не удалось определить размер основной надписи. Для этого листа выберите настройку печати Revit.");
                        paper = layout.Configure(size, settings);
                        printManager.Apply();
                        logger.Info($"Sheet {item.SheetNumber}: {size}, paper '{paper.Paper.Name}' {paper.OrientedSize}, zoom 100%, centered.");
                    }
                    logger.Info($"Submitting sheet '{item.SheetNumber} - {item.SheetName}' (element id {item.ElementId}) to printer '{printerName}'.");
                    using var fileJob = outputPaths is null ? null : new DriverOutputFile(outputPaths[item.ElementId]);
                    if (printManager.SubmitPrint(sheet))
                    {
                        printedSheetCount++;
                        if (paper is not null) expectedPaper[item.ElementId] = paper.OrientedSize;
                        if (paper is { IsExact: false })
                        {
                            largerPaper[item.ElementId] = $"{paper.Paper.Name} ({paper.OrientedSize}), 100%";
                        }
                    }
                    else
                    {
                        failures.Add(new PrintDriverFailure(item, "Драйвер или Revit отклонил задание печати."));
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(new PrintDriverFailure(item, exception.Message));
                    logger.Error($"Print driver failed for sheet element id {item.ElementId} on printer '{printerName}'.", exception);
                }
                finally
                {
                    progress?.Invoke(new PrintOperationProgressStep(
                        "Печать",
                        itemName,
                        PrintOperationProgressPhase.Completed));
                }
            }

            logger.Info($"Print driver job completed for document '{document.Title}'. Printer: {printerName}. Printed sheets: {printedSheetCount}; failures: {failures.Count}.");
            return new PrintDriverResult(printedSheetCount, failures) { LargerPaper = largerPaper, ExpectedPaper = expectedPaper };
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to configure print driver '{printerName}' for document '{document.Title}'.", exception);
            return FailAll(items, exception.Message);
        }
        finally
        {
            try
            {
                if (temporarySettings?.GetStatus() == TransactionStatus.Started)
                {
                    if (originalLayout is not null && snapshot is not null && printManager is not null)
                    {
                        if (!string.Equals(printManager.PrinterName, snapshot.PrinterName, StringComparison.OrdinalIgnoreCase))
                            printManager.SelectNewPrintDriver(snapshot.PrinterName);
                        var setup = printManager.PrintSetup;
                        setup.CurrentPrintSetting = setup.InSession;
                        originalLayout.Apply(setup.CurrentPrintSetting.PrintParameters, printManager);
                        SaveTemporaryLayout(document, setup);
                        printManager.Apply();
                    }
                    temporarySettings.RollBack();
                }
            }
            catch (Exception exception)
            {
                logger.Warning($"Could not roll back temporary print settings: {exception.Message}");
            }
            temporarySettings?.Dispose();
            if (modifiedParameters is not null && originalParameters.HasValue)
            {
                try
                {
                    modifiedParameters.ColorDepth = originalParameters.Value.Color;
                    modifiedParameters.RasterQuality = originalParameters.Value.Quality;
                    modifiedParameters.HiddenLineViews = originalParameters.Value.Processing;
                }
                catch (Exception exception)
                {
                    logger.Warning($"Could not restore print parameters: {exception.Message}");
                }
            }
            if (printManager is not null && snapshot is not null)
            {
                snapshot.Restore(printManager, logger, document.Title);
                if (originalLayout is not null)
                {
                    try
                    {
                        var restored = document.PrintManager;
                        var setup = restored.PrintSetup;
                        if (setup.CurrentPrintSetting is not PrintSetting)
                        {
                            originalLayout.Apply(setup.CurrentPrintSetting.PrintParameters, restored);
                            restored.Apply();
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.Warning($"Could not restore in-session print parameters: {exception.Message}");
                    }
                }
            }
        }
    }

    internal sealed record LayoutSnapshot(string Paper, PageOrientationType Orientation, PaperPlacementType Placement,
        ZoomType ZoomType, int Zoom, ColorDepthType Color, RasterQualityType Quality, HiddenLineViewsType Lines)
    {
        public static LayoutSnapshot Capture(PrintParameters p) => new(p.PaperSize.Name, p.PageOrientation,
            p.PaperPlacement, p.ZoomType, p.ZoomType == ZoomType.Zoom ? p.Zoom : 100,
            p.ColorDepth, p.RasterQuality, p.HiddenLineViews);

        public void Apply(PrintParameters p, PrintManager manager)
        {
            var paper = manager.PaperSizes.Cast<PaperSize>().FirstOrDefault(size => size.Name == Paper);
            if (paper is not null) p.PaperSize = paper;
            p.PageOrientation = Orientation;
            p.PaperPlacement = Placement;
            p.ZoomType = ZoomType;
            if (ZoomType == ZoomType.Zoom) p.Zoom = Zoom;
            p.ColorDepth = Color;
            p.RasterQuality = Quality;
            p.HiddenLineViews = Lines;
        }
    }

    private static void SaveTemporaryLayout(Document document, PrintSetup setup)
    {
        using var tx = new Transaction(document, "TrueBIM — исходная настройка печати");
        tx.Start();
        if (!setup.SaveAs("TrueBIM-" + Guid.NewGuid().ToString("N")) || tx.Commit() != TransactionStatus.Committed)
            throw new InvalidOperationException("Не удалось сохранить исходную настройку печати.");
    }

    internal static void ApplyRendering(PrintParameters parameters, PrintPdfExportSettings settings)
    {
        parameters.ColorDepth = settings.ColorMode switch
        {
            PrintPdfColorMode.BlackLine => ColorDepthType.BlackLine,
            PrintPdfColorMode.GrayScale => ColorDepthType.GrayScale,
            _ => ColorDepthType.Color
        };
        parameters.RasterQuality = settings.RasterQuality switch
        {
            PrintPdfRasterQuality.Low => RasterQualityType.Low,
            PrintPdfRasterQuality.Medium => RasterQualityType.Medium,
            PrintPdfRasterQuality.Presentation => RasterQualityType.Presentation,
            _ => RasterQualityType.High
        };
        parameters.HiddenLineViews = settings.AlwaysUseRaster
            ? HiddenLineViewsType.RasterProcessing : HiddenLineViewsType.VectorProcessing;
    }

    private static PrintSetting? FindPrintSetting(Document document, string printSetupName)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(PrintSetting))
            .Cast<PrintSetting>()
            .FirstOrDefault(setting => string.Equals(
                setting.Name,
                printSetupName,
                StringComparison.CurrentCultureIgnoreCase));
    }

    private static PrintDriverResult FailAll(
        IReadOnlyList<PrintDriverJobItem> items,
        string message)
    {
        return new PrintDriverResult(
            0,
            items.Select(item => new PrintDriverFailure(item, message)).ToList());
    }

    private sealed record PrintManagerSnapshot(
        string PrinterName,
        bool PrintToFile,
        PrintRange PrintRange,
        IPrintSetting? PrintSetting)
    {
        public static PrintManagerSnapshot Capture(Autodesk.Revit.DB.PrintManager printManager)
        {
            return new PrintManagerSnapshot(
                printManager.PrinterName,
                printManager.PrintToFile,
                printManager.PrintRange,
                printManager.PrintSetup.CurrentPrintSetting);
        }

        public void Restore(
            Autodesk.Revit.DB.PrintManager printManager,
            ITrueBimLogger logger,
            string documentTitle)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(PrinterName)
                    && !string.Equals(printManager.PrinterName, PrinterName, StringComparison.CurrentCultureIgnoreCase))
                {
                    printManager.SelectNewPrintDriver(PrinterName);
                }

                if (PrintSetting is not null)
                {
                    printManager.PrintSetup.CurrentPrintSetting = PrintSetting;
                }

                if (PrintToFile || printManager.IsVirtual == VirtualPrinterType.None)
                {
                    printManager.PrintToFile = PrintToFile;
                }
                printManager.PrintRange = PrintRange;
                printManager.Apply();
                logger.Info($"Restored Revit print settings for document '{documentTitle}' after driver job.");
            }
            catch (Exception exception)
            {
                logger.Warning($"Could not fully restore Revit print settings for document '{documentTitle}': {exception.Message}");
            }
        }
    }
}
