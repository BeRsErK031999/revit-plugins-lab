using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Modules.Print.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintPdfExportServiceTests
{
    [Fact]
    public void NormalizePdfFileName_AddsPdfExtension()
    {
        string fileName = PrintPdfExportService.NormalizePdfFileName("A-101_План");

        Assert.Equal("A-101_План.pdf", fileName);
    }

    [Fact]
    public void NormalizePdfFileName_KeepsExistingPdfExtension()
    {
        string fileName = PrintPdfExportService.NormalizePdfFileName("A-101.pdf");

        Assert.Equal("A-101.pdf", fileName);
    }

    [Fact]
    public void NormalizePdfFileName_RemovesUnexpectedFolderSegments()
    {
        string fileName = PrintPdfExportService.NormalizePdfFileName(@"C:\Temp\A-101.pdf");

        Assert.Equal("A-101.pdf", fileName);
    }

    [Fact]
    public void NormalizePdfFileName_RejectsEmptyFileName()
    {
        Assert.Throws<ArgumentException>(() => PrintPdfExportService.NormalizePdfFileName(" "));
    }

    [Fact]
    public void BuildCombinedPdfFileName_UsesDefaultWhenNameIsEmpty()
    {
        string fileName = PrintPdfExportService.BuildCombinedPdfFileName(" ");

        Assert.Equal("Объединенный PDF.pdf", fileName);
    }

    [Fact]
    public void BuildCombinedPdfFileName_SanitizesFileNameAndAddsPdfExtension()
    {
        string fileName = PrintPdfExportService.BuildCombinedPdfFileName("Project:Stage*One");

        Assert.Equal("Project_Stage_One.pdf", fileName);
    }

    [Fact]
    public void BuildCombinedPdfFileName_RemovesUnexpectedFolderSegments()
    {
        string fileName = PrintPdfExportService.BuildCombinedPdfFileName(@"C:\Temp\Combined.pdf");

        Assert.Equal("Combined.pdf", fileName);
    }

    [Fact]
    public void GetModeDisplayName_ReturnsSeparateAndCombinedLabels()
    {
        Assert.Equal("отдельные PDF", PrintPdfExportService.GetModeDisplayName(PrintPdfExportMode.SeparateFiles));
        Assert.Equal("один PDF", PrintPdfExportService.GetModeDisplayName(PrintPdfExportMode.CombinedFile));
    }
}
