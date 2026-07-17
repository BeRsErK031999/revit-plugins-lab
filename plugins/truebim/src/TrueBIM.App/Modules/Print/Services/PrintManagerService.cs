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
        Action<PrintOperationProgressStep>? progress = null)
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
        try
        {
            printManager = document.PrintManager;
            snapshot = PrintManagerSnapshot.Capture(printManager);
            printManager.SelectNewPrintDriver(printerName);
            printManager.PrintToFile = false;

            if (!string.IsNullOrWhiteSpace(printSetupName))
            {
                PrintSetting? printSetting = FindPrintSetting(document, printSetupName!);
                if (printSetting is null)
                {
                    return FailAll(items, $"Настройка печати «{printSetupName}» отсутствует в документе «{document.Title}».");
                }

                printManager.PrintSetup.CurrentPrintSetting = printSetting;
            }

            logger.Info($"Print driver job started for document '{document.Title}'. Printer: {printerName}. Print setup: {printSetupName ?? "current document setting"}. Range: selected sheets. Sheets: {items.Count}.");
            int printedSheetCount = 0;
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

                    logger.Info($"Submitting sheet '{item.SheetNumber} - {item.SheetName}' (element id {item.ElementId}) to printer '{printerName}'.");
                    if (printManager.SubmitPrint(sheet))
                    {
                        printedSheetCount++;
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
            return new PrintDriverResult(printedSheetCount, failures);
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to configure print driver '{printerName}' for document '{document.Title}'.", exception);
            return FailAll(items, exception.Message);
        }
        finally
        {
            if (printManager is not null && snapshot is not null)
            {
                snapshot.Restore(printManager, logger, document.Title);
            }
        }
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

                printManager.PrintToFile = PrintToFile;
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
