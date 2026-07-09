using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyBackupCleanupPreviewServiceTests
{
    [Fact]
    public void Preview_FindsBackupFilesRecursivelyAndReportsTotals()
    {
        using TempDirectory temp = new();
        string library = Path.Combine(temp.Path, "Library");
        string nested = Path.Combine(library, "Doors");
        Directory.CreateDirectory(nested);

        string primaryPath = Path.Combine(nested, "Door.rfa");
        string backupPath = Path.Combine(nested, "Door.0001.rfa");
        string secondBackupPath = Path.Combine(library, "Window.0123.rfa");
        File.WriteAllBytes(primaryPath, [1]);
        File.WriteAllBytes(backupPath, new byte[1024]);
        File.WriteAllBytes(secondBackupPath, new byte[512]);
        File.WriteAllBytes(Path.Combine(library, "Chair.0001.txt"), [1]);

        FamilyBackupCleanupPreviewResult result = new FamilyBackupCleanupPreviewService().Preview(
            [new FamilyLibraryFolder { Path = library, IsEnabled = true }],
            []);

        Assert.Equal(["Door.0001.rfa", "Window.0123.rfa"], result.Files.Select(file => file.Name).OrderBy(name => name));
        Assert.Equal(1536, result.TotalSizeBytes);
        Assert.Equal("1.5 KB", result.TotalSizeDisplay);
        Assert.Empty(result.Warnings);

        FamilyBackupFileItem doorBackup = result.Files.Single(file => file.Name == "Door.0001.rfa");
        Assert.Equal("Door", doorBackup.FamilyName);
        Assert.Equal(1, doorBackup.BackupIndex);
        Assert.Equal(primaryPath, doorBackup.PrimaryFilePath);
        Assert.Equal(backupPath, doorBackup.FilePath);
    }

    [Fact]
    public void Preview_IncludesExplicitBackupFilesDeduplicatesAndWarnsAboutMissingSources()
    {
        using TempDirectory temp = new();
        string library = Path.Combine(temp.Path, "Library");
        Directory.CreateDirectory(library);

        string folderBackupPath = Path.Combine(library, "Door.0002.rfa");
        string explicitBackupPath = Path.Combine(temp.Path, "Chair.0003.rfa");
        string missingBackupPath = Path.Combine(temp.Path, "Table.0004.rfa");
        File.WriteAllBytes(folderBackupPath, [1]);
        File.WriteAllBytes(explicitBackupPath, [1]);

        FamilyBackupCleanupPreviewResult result = new FamilyBackupCleanupPreviewService().Preview(
            [
                new FamilyLibraryFolder { Path = library, IsEnabled = true },
                new FamilyLibraryFolder { Path = Path.Combine(temp.Path, "Missing"), IsEnabled = true },
                new FamilyLibraryFolder { Path = Path.Combine(temp.Path, "Disabled"), IsEnabled = false }
            ],
            [
                new FamilyLibraryFile { Path = folderBackupPath, IsEnabled = true },
                new FamilyLibraryFile { Path = explicitBackupPath, IsEnabled = true },
                new FamilyLibraryFile { Path = missingBackupPath, IsEnabled = true },
                new FamilyLibraryFile { Path = Path.Combine(temp.Path, "Primary.rfa"), IsEnabled = true }
            ]);

        Assert.Equal(["Chair.0003.rfa", "Door.0002.rfa"], result.Files.Select(file => file.Name).OrderBy(name => name));
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Папка не найдена:", StringComparison.CurrentCulture));
        Assert.Contains(result.Warnings, warning => warning.StartsWith("Backup-файл не найден:", StringComparison.CurrentCulture));
    }

    [Theory]
    [InlineData(@"C:\Library\Door.0001.rfa", true)]
    [InlineData(@"C:\Library\Door.0100.RFA", true)]
    [InlineData(@"C:\Library\Door.rfa", false)]
    [InlineData(@"C:\Library\Door.001.rfa", false)]
    [InlineData(@"C:\Library\Door.0001.txt", false)]
    public void IsBackupFamilyFile_MatchesFourDigitRevitBackupPattern(string filePath, bool expected)
    {
        Assert.Equal(expected, FamilyBackupCleanupPreviewService.IsBackupFamilyFile(filePath));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-family-backup-tests-" + Guid.NewGuid());
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
