using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintCadExportServiceTests
{
    [Theory]
    [InlineData(true, false, DwfExportTransactionMode.UseExistingTransaction)]
    [InlineData(false, false, DwfExportTransactionMode.StartTemporaryTransaction)]
    [InlineData(false, true, DwfExportTransactionMode.RejectReadOnlyDocument)]
    public void DwfExportTransactionPolicy_ResolvesDocumentContext(
        bool documentIsModifiable,
        bool documentIsReadOnly,
        DwfExportTransactionMode expected)
    {
        DwfExportTransactionMode actual = DwfExportTransactionPolicy.Resolve(
            documentIsModifiable,
            documentIsReadOnly);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NormalizeCadFileName_AddsDwgExtension()
    {
        string fileName = PrintCadExportService.NormalizeCadFileName("A-101_План", PrintCadExportFormat.Dwg);

        Assert.Equal("A-101_План.dwg", fileName);
    }

    [Fact]
    public void NormalizeCadFileName_AddsDxfExtension()
    {
        string fileName = PrintCadExportService.NormalizeCadFileName("A-101_План", PrintCadExportFormat.Dxf);

        Assert.Equal("A-101_План.dxf", fileName);
    }

    [Fact]
    public void NormalizeCadFileName_AddsDwfExtension()
    {
        string fileName = PrintCadExportService.NormalizeCadFileName("A-101_План", PrintCadExportFormat.Dwf);

        Assert.Equal("A-101_План.dwf", fileName);
    }

    [Fact]
    public void NormalizeCadFileName_KeepsExistingFormatExtension()
    {
        string fileName = PrintCadExportService.NormalizeCadFileName("A-101.DWG", PrintCadExportFormat.Dwg);

        Assert.Equal("A-101.DWG", fileName);
    }

    [Fact]
    public void NormalizeCadFileName_RemovesUnexpectedFolderSegments()
    {
        string fileName = PrintCadExportService.NormalizeCadFileName(@"C:\Temp\A-101.dxf", PrintCadExportFormat.Dxf);

        Assert.Equal("A-101.dxf", fileName);
    }

    [Fact]
    public void NormalizeCadFileName_RejectsEmptyFileName()
    {
        Assert.Throws<ArgumentException>(() => PrintCadExportService.NormalizeCadFileName(" ", PrintCadExportFormat.Dwg));
    }
}
