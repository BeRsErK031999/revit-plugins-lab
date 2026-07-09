using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyTypeCatalogReaderTests
{
    [Fact]
    public void ReadForFamily_ReadsTypeNamesFromAdjacentCatalog()
    {
        using TempDirectory temp = new();
        string familyPath = Path.Combine(temp.Path, "Door.rfa");
        string catalogPath = Path.Combine(temp.Path, "Door.txt");
        File.WriteAllText(familyPath, "not a real rfa");
        File.WriteAllLines(
            catalogPath,
            [
                "; comment",
                ",Width##length##millimeters,Height##length##millimeters",
                "\"900, left\",900,2100",
                "1000 right,1000,2100",
                "\"900, left\",900,2100"
            ]);

        var result = new FamilyTypeCatalogReader().ReadForFamily(familyPath);

        Assert.Equal(catalogPath, result.Path);
        Assert.Equal(["1000 right", "900, left"], result.TypeNames);
    }

    [Fact]
    public void ReadForFamily_ReturnsEmptyInfoWhenCatalogIsMissing()
    {
        using TempDirectory temp = new();
        string familyPath = Path.Combine(temp.Path, "Chair.rfa");
        File.WriteAllText(familyPath, "not a real rfa");

        var result = new FamilyTypeCatalogReader().ReadForFamily(familyPath);

        Assert.False(result.Exists);
        Assert.Empty(result.TypeNames);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-family-catalog-tests-" + Guid.NewGuid());
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
}
