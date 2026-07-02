using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintCadExportSetupServiceTests
{
    [Fact]
    public void BuildSetupOptions_ReturnsDefaultFallbackWhenSetupListIsEmpty()
    {
        IReadOnlyList<PrintCadExportSetupOption> options = PrintCadExportSetupService.BuildSetupOptions([]);

        PrintCadExportSetupOption option = Assert.Single(options);
        Assert.True(option.IsDefault);
        Assert.Null(option.SetupName);
        Assert.Equal(PrintCadExportSetupService.DefaultWithoutSetupsDisplayName, option.DisplayName);
    }

    [Fact]
    public void BuildSetupOptions_NormalizesSetupNames()
    {
        IReadOnlyList<PrintCadExportSetupOption> options = PrintCadExportSetupService.BuildSetupOptions(
        [
            "  Office DWG  ",
            "",
            "office dwg",
            "DXF Export"
        ]);

        Assert.Collection(
            options,
            option => Assert.Equal(PrintCadExportSetupService.DefaultDisplayName, option.DisplayName),
            option => Assert.Equal("Office DWG", option.SetupName),
            option => Assert.Equal("DXF Export", option.SetupName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeSetupName_ReturnsNullForMissingSetup(string? setupName)
    {
        Assert.Null(PrintCadExportSetupService.NormalizeSetupName(setupName));
    }

    [Fact]
    public void GetSelectionDisplayName_ShowsDefaultAndSelectedSetup()
    {
        PrintCadExportSetupOption defaultOption = new(null, PrintCadExportSetupService.DefaultDisplayName);
        PrintCadExportSetupOption selectedOption = new("Office DWG", "Office DWG");

        Assert.Equal(
            "DWG: По умолчанию",
            PrintCadExportSetupService.GetSelectionDisplayName(PrintCadExportFormat.Dwg, defaultOption));
        Assert.Equal(
            "DXF: Office DWG",
            PrintCadExportSetupService.GetSelectionDisplayName(PrintCadExportFormat.Dxf, selectedOption));
    }
}
