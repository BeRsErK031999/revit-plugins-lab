using TrueBIM.App.Modules.SharedParameters.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SharedParameters;

public sealed class SharedParameterFamilyFileScannerTests
{
    private readonly SharedParameterFamilyFileScanner scanner = new();

    [Theory]
    [InlineData(@"C:\Families\Door.rfa", true)]
    [InlineData(@"C:\Families\Door.RFA", true)]
    [InlineData(@"C:\Families\Door.0001.rfa", false)]
    [InlineData(@"C:\Families\~Door.rfa", false)]
    [InlineData(@"C:\Families\Door.tmp.rfa", false)]
    [InlineData(@"C:\Families\Door.rvt", false)]
    public void IsSupportedFamilyPath_FiltersBackupsAndTemporaryFiles(string path, bool expected)
    {
        Assert.Equal(expected, scanner.IsSupportedFamilyPath(path));
    }
}
