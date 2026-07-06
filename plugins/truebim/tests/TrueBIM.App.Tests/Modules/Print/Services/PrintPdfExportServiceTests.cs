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
        Assert.Equal("отдельные PDF и один общий", PrintPdfExportService.GetModeDisplayName(PrintPdfExportMode.SeparateAndCombined));
    }

    [Fact]
    public void DefaultSettings_UseColorHighQualityAndVectorOutput()
    {
        PrintPdfExportSettings settings = PrintPdfExportService.DefaultSettings;

        Assert.Equal(PrintPdfColorMode.Color, settings.ColorMode);
        Assert.Equal(PrintPdfRasterQuality.High, settings.RasterQuality);
        Assert.False(settings.AlwaysUseRaster);
    }

    [Fact]
    public void GetSettingsDisplayName_DescribesVectorSettings()
    {
        PrintPdfExportSettings settings = new(
            PrintPdfColorMode.GrayScale,
            PrintPdfRasterQuality.Medium,
            AlwaysUseRaster: false);

        string displayName = PrintPdfExportService.GetSettingsDisplayName(settings);

        Assert.Equal("оттенки серого, среднее качество, вектор", displayName);
    }

    [Fact]
    public void GetSettingsDisplayName_DescribesRasterSettings()
    {
        PrintPdfExportSettings settings = new(
            PrintPdfColorMode.BlackLine,
            PrintPdfRasterQuality.Presentation,
            AlwaysUseRaster: true);

        string displayName = PrintPdfExportService.GetSettingsDisplayName(settings);

        Assert.Equal("черные линии, презентационное качество, растр", displayName);
    }
}
