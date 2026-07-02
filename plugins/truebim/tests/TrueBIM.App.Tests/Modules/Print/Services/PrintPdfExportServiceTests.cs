using TrueBIM.App.Modules.Print.Services;
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
}
