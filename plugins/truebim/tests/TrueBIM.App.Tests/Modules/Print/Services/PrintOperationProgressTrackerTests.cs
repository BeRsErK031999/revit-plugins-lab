using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintOperationProgressTrackerTests
{
    [Fact]
    public void CalculateExportFileCount_CountsSeparateFormatsPerSheet()
    {
        int result = PrintOperationProgressTracker.CalculateExportFileCount(
            selectedSheetCount: 5,
            selectedSourceCount: 2,
            exportPdf: true,
            pdfMode: PrintPdfExportMode.SeparateFiles,
            exportDwg: true,
            combineDwg: false,
            exportDxf: true,
            exportDwf: true);

        Assert.Equal(20, result);
    }

    [Fact]
    public void CalculateExportFileCount_CountsCombinedFilesPerSource()
    {
        int result = PrintOperationProgressTracker.CalculateExportFileCount(
            selectedSheetCount: 5,
            selectedSourceCount: 2,
            exportPdf: true,
            pdfMode: PrintPdfExportMode.CombinedFile,
            exportDwg: true,
            combineDwg: true,
            exportDxf: false,
            exportDwf: false);

        Assert.Equal(4, result);
    }

    [Fact]
    public void CompleteStep_IncrementsProcessedAndReducesRemainingCount()
    {
        PrintOperationProgressTracker tracker = new(3, PrintOperationProgressUnit.File);

        PrintOperationProgressSnapshot started = tracker.BeginStep("PDF", "A101.pdf");
        PrintOperationProgressSnapshot completed = tracker.CompleteStep("PDF", "A101.pdf");

        Assert.Equal(0, started.CompletedCount);
        Assert.Equal(3, started.RemainingCount);
        Assert.Equal(1, completed.CompletedCount);
        Assert.Equal(2, completed.RemainingCount);
        Assert.Equal("A101.pdf", completed.ItemName);
    }

    [Fact]
    public void CompleteStep_DoesNotExceedTotalCount()
    {
        PrintOperationProgressTracker tracker = new(1, PrintOperationProgressUnit.Sheet);

        tracker.CompleteStep("Печать", "А101");
        PrintOperationProgressSnapshot result = tracker.CompleteStep("Печать", "А102");

        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.RemainingCount);
    }

    [Theory]
    [InlineData(1, PrintOperationProgressUnit.File, "файл")]
    [InlineData(2, PrintOperationProgressUnit.File, "файла")]
    [InlineData(5, PrintOperationProgressUnit.File, "файлов")]
    [InlineData(11, PrintOperationProgressUnit.File, "файлов")]
    [InlineData(21, PrintOperationProgressUnit.Sheet, "лист")]
    [InlineData(24, PrintOperationProgressUnit.Sheet, "листа")]
    [InlineData(30, PrintOperationProgressUnit.Sheet, "листов")]
    public void GetUnitDisplayName_UsesRussianPluralForm(
        int count,
        PrintOperationProgressUnit unit,
        string expected)
    {
        Assert.Equal(expected, PrintOperationProgressTracker.GetUnitDisplayName(count, unit));
    }
}
