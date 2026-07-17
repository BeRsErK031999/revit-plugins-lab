using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintDriverCatalogServiceTests
{
    [Theory]
    [InlineData("Microsoft Print to PDF")]
    [InlineData("Adobe PDF")]
    [InlineData("PDFCreator")]
    [InlineData("Bluebeam PDF")]
    public void IsPdfDriverName_ReturnsTrueForPdfDrivers(string printerName)
    {
        Assert.True(PrintDriverCatalogService.IsPdfDriverName(printerName));
    }

    [Theory]
    [InlineData("HP LaserJet")]
    [InlineData("Canon Plotter")]
    [InlineData("")]
    [InlineData(null)]
    public void IsPdfDriverName_ReturnsFalseForOtherNames(string? printerName)
    {
        Assert.False(PrintDriverCatalogService.IsPdfDriverName(printerName));
    }
}
