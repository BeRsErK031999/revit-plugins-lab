using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintOperationProgressTracker
{
    private int completedCount;

    public PrintOperationProgressTracker(int totalCount, PrintOperationProgressUnit unit)
    {
        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        }

        TotalCount = totalCount;
        Unit = unit;
    }

    public int TotalCount { get; }

    public PrintOperationProgressUnit Unit { get; }

    public PrintOperationProgressSnapshot BeginStep(string operationName, string itemName)
    {
        return CreateSnapshot(operationName, itemName);
    }

    public PrintOperationProgressSnapshot CompleteStep(string operationName, string itemName)
    {
        completedCount = Math.Min(completedCount + 1, TotalCount);
        return CreateSnapshot(operationName, itemName);
    }

    public PrintOperationProgressSnapshot Finish(string operationName)
    {
        completedCount = TotalCount;
        return CreateSnapshot(operationName, string.Empty);
    }

    public static int CalculateExportFileCount(
        int selectedSheetCount,
        int selectedSourceCount,
        bool exportPdf,
        PrintPdfExportMode pdfMode,
        bool exportDwg,
        bool combineDwg,
        bool exportDxf,
        bool exportDwf)
    {
        if (selectedSheetCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedSheetCount));
        }

        if (selectedSourceCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectedSourceCount));
        }

        int sourceCount = selectedSheetCount == 0
            ? 0
            : Math.Max(1, selectedSourceCount);
        int fileCount = 0;
        if (exportPdf)
        {
            fileCount += pdfMode switch
            {
                PrintPdfExportMode.SeparateFiles => selectedSheetCount,
                PrintPdfExportMode.CombinedFile => sourceCount,
                PrintPdfExportMode.SeparateAndCombined => selectedSheetCount + sourceCount,
                _ => throw new ArgumentOutOfRangeException(nameof(pdfMode), pdfMode, "Unsupported PDF export mode.")
            };
        }

        if (exportDwg)
        {
            fileCount += combineDwg ? sourceCount : selectedSheetCount;
        }

        if (exportDxf)
        {
            fileCount += selectedSheetCount;
        }

        if (exportDwf)
        {
            fileCount += selectedSheetCount;
        }

        return fileCount;
    }

    public static string GetUnitDisplayName(int count, PrintOperationProgressUnit unit)
    {
        int absoluteCount = Math.Abs(count);
        int lastTwoDigits = absoluteCount % 100;
        int lastDigit = absoluteCount % 10;
        if (unit == PrintOperationProgressUnit.File)
        {
            return lastTwoDigits is >= 11 and <= 14
                ? "файлов"
                : lastDigit switch
                {
                    1 => "файл",
                    2 or 3 or 4 => "файла",
                    _ => "файлов"
                };
        }

        return lastTwoDigits is >= 11 and <= 14
            ? "листов"
            : lastDigit switch
            {
                1 => "лист",
                2 or 3 or 4 => "листа",
                _ => "листов"
            };
    }

    private PrintOperationProgressSnapshot CreateSnapshot(string operationName, string itemName)
    {
        return new PrintOperationProgressSnapshot(
            completedCount,
            TotalCount,
            Math.Max(0, TotalCount - completedCount),
            Unit,
            operationName,
            itemName);
    }
}
