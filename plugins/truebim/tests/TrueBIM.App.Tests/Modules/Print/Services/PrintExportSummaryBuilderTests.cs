using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintExportSummaryBuilderTests
{
    [Fact]
    public void Build_ShowsShortPathListAndFullExpandedReport()
    {
        string[] exportedFiles =
        [
            @"C:\Exports\A101.pdf",
            @"C:\Exports\A102.dwg",
            @"C:\Exports\A103.dxf",
            @"C:\Exports\A104.dwf"
        ];

        PrintExportSummary summary = PrintExportSummaryBuilder.Build(
            exportedFiles,
            failureCount: 0,
            []);

        Assert.Equal("Экспорт завершен", summary.MainInstruction);
        Assert.Equal(4, summary.ExportedFileCount);
        Assert.Contains(@"C:\Exports\A101.pdf", summary.MainContent, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Exports\A104.dwf", summary.MainContent, StringComparison.Ordinal);
        Assert.Contains("Ещё файлов: 1", summary.MainContent, StringComparison.CurrentCulture);
        Assert.NotNull(summary.ExpandedContent);
        Assert.Contains(@"4. C:\Exports\A104.dwf", summary.ExpandedContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DeduplicatesActualPathsIgnoringCase()
    {
        PrintExportSummary summary = PrintExportSummaryBuilder.Build(
            [@"C:\Exports\A101.pdf", @"c:\exports\A101.pdf", "  "],
            failureCount: 0,
            []);

        Assert.Equal(1, summary.ExportedFileCount);
        Assert.Contains("Экспортировано файлов: 1", summary.MainContent, StringComparison.CurrentCulture);
        Assert.Null(summary.ExpandedContent);
    }

    [Fact]
    public void Build_ReportsFailuresWhenNoFilesWereCreated()
    {
        PrintExportSummary summary = PrintExportSummaryBuilder.Build(
            [],
            failureCount: 1,
            ["PDF Лист A101: Revit не подтвердил экспорт."]);

        Assert.Equal("Экспорт не выполнен", summary.MainInstruction);
        Assert.Equal(0, summary.ExportedFileCount);
        Assert.Contains("Файлы не созданы.", summary.MainContent, StringComparison.CurrentCulture);
        Assert.Contains("Ошибок: 1", summary.MainContent, StringComparison.CurrentCulture);
        Assert.Contains("Revit не подтвердил экспорт", summary.MainContent, StringComparison.CurrentCulture);
    }
}
