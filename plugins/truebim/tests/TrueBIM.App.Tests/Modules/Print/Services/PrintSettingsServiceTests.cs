using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintSettingsServiceTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenSettingsFileDoesNotExist()
    {
        using TempDirectory temp = new();
        PrintSettingsService service = new(Path.Combine(temp.Path, "print-settings.json"), new TestLogger());

        Assert.False(service.SettingsFileExists);
        PrintSettings settings = service.Load();

        Assert.Equal(PrintFileNameTemplateService.DefaultTemplate, settings.FileNameMask);
        Assert.False(settings.IncludePlaceholders);
        Assert.True(settings.ExportPdf);
        Assert.False(settings.ExportDwg);
        Assert.False(settings.ExportDxf);
        Assert.False(settings.ExportDwf);
        Assert.False(settings.CombineDwg);
        Assert.Equal(PrintFileNameTemplateService.DefaultCombinedTemplate, settings.CombinedPdfFileName);
        Assert.Equal(PrintFileNameTemplateService.DefaultCombinedTemplate, settings.CombinedDwgFileNameMask);
        Assert.Equal(PrintPdfExportService.DefaultSettings.ColorMode, settings.PdfColorMode);
    }

    [Fact]
    public void Save_PersistsNormalizedSettings()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "nested", "print-settings.json");
        PrintSettingsService service = new(settingsPath, new TestLogger());

        service.Save(new PrintSettings(
            @"C:\Exports",
            "  {SheetNumber}_{SheetName}  ",
            IncludePlaceholders: true,
            ExportPdf: false,
            CombinePdf: true,
            "  {Номер проекта}_{Имя документа}  ",
            PrintPdfColorMode.GrayScale,
            PrintPdfRasterQuality.Medium,
            AlwaysUseRasterPdf: true,
            ExportDwg: true,
            ExportDxf: true,
            ExportDwf: true,
            CombineDwg: true,
            DwgSetupName: "  Office DWG  ",
            DxfSetupName: "  Office DXF  ",
            CombinedDwgFileNameMask: "  {Номер проекта}_{Имя документа}  "));

        Assert.True(service.SettingsFileExists);
        PrintSettings reloadedSettings = new PrintSettingsService(settingsPath, new TestLogger()).Load();
        Assert.Equal(@"C:\Exports", reloadedSettings.ExportFolder);
        Assert.Equal("{SheetNumber}_{SheetName}", reloadedSettings.FileNameMask);
        Assert.True(reloadedSettings.IncludePlaceholders);
        Assert.False(reloadedSettings.ExportPdf);
        Assert.True(reloadedSettings.CombinePdf);
        Assert.Equal("{Номер проекта}_{Имя документа}", reloadedSettings.CombinedPdfFileName);
        Assert.Equal(PrintPdfColorMode.GrayScale, reloadedSettings.PdfColorMode);
        Assert.Equal(PrintPdfRasterQuality.Medium, reloadedSettings.PdfRasterQuality);
        Assert.True(reloadedSettings.AlwaysUseRasterPdf);
        Assert.True(reloadedSettings.ExportDwg);
        Assert.True(reloadedSettings.ExportDxf);
        Assert.True(reloadedSettings.ExportDwf);
        Assert.True(reloadedSettings.CombineDwg);
        Assert.Equal("Office DWG", reloadedSettings.DwgSetupName);
        Assert.Equal("Office DXF", reloadedSettings.DxfSetupName);
        Assert.Equal("{Номер проекта}_{Имя документа}", reloadedSettings.CombinedDwgFileNameMask);
        Assert.DoesNotContain(
            "exportSeparatePdfWithCombined",
            File.ReadAllText(settingsPath),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_NormalizesBlankSettings()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "print-settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "exportFolder": "   ",
              "fileNameMask": "   ",
              "combinedPdfFileName": "   ",
              "combinedDwgFileNameMask": "   ",
              "dwgSetupName": "  Office DWG  ",
              "dxfSetupName": "   "
            }
            """);

        PrintSettings settings = new PrintSettingsService(settingsPath, new TestLogger()).Load();

        Assert.Null(settings.ExportFolder);
        Assert.Equal(PrintFileNameTemplateService.DefaultTemplate, settings.FileNameMask);
        Assert.Equal(PrintFileNameTemplateService.DefaultCombinedTemplate, settings.CombinedPdfFileName);
        Assert.Equal(PrintFileNameTemplateService.DefaultCombinedTemplate, settings.CombinedDwgFileNameMask);
        Assert.Equal("Office DWG", settings.DwgSetupName);
        Assert.Null(settings.DxfSetupName);
    }

    [Fact]
    public void Load_UsesCombinedDefaultsForLegacySettingsFile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "print-settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "fileNameMask": "{Номер листа}",
              "exportDwg": true,
              "combineDwg": true
            }
            """);

        PrintSettings settings = new PrintSettingsService(settingsPath, new TestLogger()).Load();

        Assert.True(settings.ExportDwg);
        Assert.True(settings.CombineDwg);
        Assert.Equal(PrintFileNameTemplateService.DefaultCombinedTemplate, settings.CombinedPdfFileName);
        Assert.Equal(PrintFileNameTemplateService.DefaultCombinedTemplate, settings.CombinedDwgFileNameMask);
    }

    [Fact]
    public void Load_PreservesLegacyCombinedPdfLiteralAsMask()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "print-settings.json");
        File.WriteAllText(settingsPath, "{ \"combinedPdfFileName\": \"Legacy package.pdf\" }");

        PrintSettings settings = new PrintSettingsService(settingsPath, new TestLogger()).Load();

        Assert.Equal("Legacy package.pdf", settings.CombinedPdfFileName);
    }

    [Fact]
    public void Load_CollapsesLegacySeparateAndCombinedPdfToCombinedFlag()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "print-settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "exportPdf": true,
              "combinePdf": true,
              "exportSeparatePdfWithCombined": true
            }
            """);

        PrintSettings settings = new PrintSettingsService(settingsPath, new TestLogger()).Load();

        Assert.True(settings.ExportPdf);
        Assert.True(settings.CombinePdf);
    }

    [Fact]
    public void CreateSettingsPath_UsesVersionedTrueBimFolder()
    {
        string settingsPath = PrintSettingsService.CreateSettingsPath("2025");

        Assert.EndsWith(Path.Combine("TrueBIM", "2025", "print-settings.json"), settingsPath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-tests-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TestLogger : ITrueBimLogger
    {
        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
