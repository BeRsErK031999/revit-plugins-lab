using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyLibraryScannerTests
{
    [Fact]
    public void Scan_FindsRfaFilesRecursivelyAndAppliesFavoriteAndHistory()
    {
        using TempDirectory temp = new();
        string library = Path.Combine(temp.Path, "Library");
        string nested = Path.Combine(library, "Doors");
        Directory.CreateDirectory(nested);
        string familyPath = Path.Combine(nested, "Door A.rfa");
        File.WriteAllText(familyPath, "not a real rfa");
        File.WriteAllText(Path.Combine(nested, "Door A.txt"), "ignore");
        DateTimeOffset loadedAtUtc = new(2026, 7, 8, 5, 0, 0, TimeSpan.Zero);

        FamilyLibraryScanResult result = new FamilyLibraryScanner().Scan(
            [
                new FamilyLibraryFolder { Path = library, IsEnabled = true },
                new FamilyLibraryFolder { Path = Path.Combine(temp.Path, "Disabled"), IsEnabled = false },
                new FamilyLibraryFolder { Path = Path.Combine(temp.Path, "Missing"), IsEnabled = true }
            ],
            new HashSet<string>([FamilyPathNormalizer.Normalize(familyPath)], FamilyPathNormalizer.Comparer),
            new Dictionary<string, DateTimeOffset>(FamilyPathNormalizer.Comparer)
            {
                [FamilyPathNormalizer.Normalize(familyPath)] = loadedAtUtc
            });

        FamilyFileItem item = Assert.Single(result.Files);
        Assert.Equal("Door A", item.Name);
        Assert.Equal("Двери", item.Category);
        Assert.True(item.IsFavorite);
        Assert.Equal(loadedAtUtc, item.LastLoadedAtUtc);
        Assert.Equal(1, result.ScannedFolderCount);
        Assert.Equal(1, result.MissingFolderCount);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Scan_IncludesExplicitRfaFilesAndDeduplicatesFolderFiles()
    {
        using TempDirectory temp = new();
        string library = Path.Combine(temp.Path, "Library");
        Directory.CreateDirectory(library);
        string folderFamilyPath = Path.Combine(library, "Door A.rfa");
        string fileFamilyPath = Path.Combine(temp.Path, "Single Chair.rfa");
        string missingFamilyPath = Path.Combine(temp.Path, "Missing.rfa");
        File.WriteAllText(folderFamilyPath, "not a real rfa");
        File.WriteAllText(fileFamilyPath, "not a real rfa");

        FamilyLibraryScanResult result = new FamilyLibraryScanner().Scan(
            [new FamilyLibraryFolder { Path = library, IsEnabled = true }],
            [
                new FamilyLibraryFile { Path = folderFamilyPath, IsEnabled = true },
                new FamilyLibraryFile { Path = fileFamilyPath, IsEnabled = true },
                new FamilyLibraryFile { Path = missingFamilyPath, IsEnabled = true }
            ],
            new HashSet<string>(FamilyPathNormalizer.Comparer),
            new Dictionary<string, DateTimeOffset>(FamilyPathNormalizer.Comparer));

        Assert.Equal(["Door A", "Single Chair"], result.Files.Select(file => file.Name).OrderBy(name => name));
        Assert.Equal(1, result.ScannedFolderCount);
        Assert.Equal(1, result.ScannedFileCount);
        Assert.Equal(1, result.MissingFileCount);
        Assert.Single(result.Warnings);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-family-tests-" + Guid.NewGuid());
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
