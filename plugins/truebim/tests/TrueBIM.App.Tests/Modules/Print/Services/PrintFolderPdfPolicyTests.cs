using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintFolderPdfPolicyTests
{
    [Theory]
    [InlineData("PDFCreator")]
    [InlineData("PDFCreator (копия 1)")]
    [InlineData("Adobe PDF")]
    [InlineData("Bluebeam PDF")]
    public void PostScriptDriversUseNativePdfOnlyWithAutomaticPaperOnSupportedRevit(string printer)
    {
        Assert.Equal(PrintFolderPdfRoute.NativeExport, PrintFolderPdfPolicy.GetRoute(printer, true, true));
        Assert.Equal(PrintFolderPdfRoute.Unsupported, PrintFolderPdfPolicy.GetRoute(printer, false, true));
        Assert.Equal(PrintFolderPdfRoute.Unsupported, PrintFolderPdfPolicy.GetRoute(printer, true, false));
        Assert.Equal(PrintFolderPdfRoute.Unsupported, PrintFolderPdfPolicy.GetRoute(printer, false, false));
    }

    [Theory]
    [InlineData("Microsoft Print to PDF")]
    [InlineData("PDF24")]
    public void SupportedDriversKeepTheirSettingsAndPre2022Support(string printer)
    {
        foreach (bool automatic in new[] { false, true })
        foreach (bool native in new[] { false, true })
            Assert.Equal(PrintFolderPdfRoute.Driver, PrintFolderPdfPolicy.GetRoute(printer, automatic, native));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("HP LaserJet")]
    public void MissingOrPhysicalPrintersCannotSilentlyBecomePdfExport(string? printer)
    {
        Assert.Equal(PrintFolderPdfRoute.Unsupported, PrintFolderPdfPolicy.GetRoute(printer, true, true));
    }
}
