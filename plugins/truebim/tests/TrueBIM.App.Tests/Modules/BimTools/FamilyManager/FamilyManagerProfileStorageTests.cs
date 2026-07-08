using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyManagerProfileStorageTests
{
    [Fact]
    public void Normalize_DeduplicatesFoldersAndFavorites()
    {
        FamilyManagerProfile profile = new()
        {
            LibraryFolders =
            [
                new FamilyLibraryFolder { Path = @" C:\Lib\Doors\ ", IsEnabled = true },
                new FamilyLibraryFolder { Path = @"C:\Lib\Doors", IsEnabled = false }
            ],
            FavoritePaths = [@"C:\Lib\Doors\Door.rfa"],
            CachedFiles =
            [
                new FamilyFileItem
                {
                    FilePath = @"C:\Lib\Doors\Door.rfa",
                    Name = "Door",
                    DirectoryPath = @"C:\Lib\Doors",
                    Category = "Двери",
                    MetadataUpdatedAtUtc = new DateTimeOffset(2026, 7, 8, 6, 0, 0, TimeSpan.Zero),
                    CachedTypes =
                    [
                        new FamilyTypeInfo(0, "  900x2100  "),
                        new FamilyTypeInfo(0, "900x2100"),
                        new FamilyTypeInfo(0, "1000x2100"),
                        new FamilyTypeInfo(0, " ")
                    ],
                    IsFavorite = true
                }
            ]
        };

        FamilyManagerProfile normalized = FamilyManagerProfileStorage.Normalize(profile);

        Assert.Single(normalized.LibraryFolders);
        Assert.Single(normalized.FavoritePaths);
        Assert.Single(normalized.CachedFiles);
        Assert.True(normalized.CachedFiles[0].IsFavorite);
        Assert.Equal(["1000x2100", "900x2100"], normalized.CachedFiles[0].CachedTypes.Select(type => type.Name));
        Assert.NotNull(normalized.CachedFiles[0].MetadataUpdatedAtUtc);
    }

    [Fact]
    public void Save_RoundTripsProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        FamilyManagerProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new FamilyManagerProfile
        {
            LibraryFolders = [new FamilyLibraryFolder { Path = temp.Path, IsEnabled = true }],
            CachedFiles =
            [
                new FamilyFileItem
                {
                    FilePath = Path.Combine(temp.Path, "Chair.rfa"),
                    Name = "Chair",
                    DirectoryPath = temp.Path,
                    Category = "Мебель",
                    MetadataUpdatedAtUtc = new DateTimeOffset(2026, 7, 8, 7, 0, 0, TimeSpan.Zero),
                    CachedTypes = [new FamilyTypeInfo(0, "Default")],
                    IsFavorite = true
                }
            ]
        });

        FamilyManagerProfile loaded = storage.Load();

        Assert.Single(loaded.LibraryFolders);
        Assert.Single(loaded.CachedFiles);
        Assert.Single(loaded.FavoritePaths);
        Assert.Equal("Chair", loaded.CachedFiles[0].Name);
        Assert.Equal("Default", Assert.Single(loaded.CachedFiles[0].CachedTypes).Name);
        Assert.NotNull(loaded.CachedFiles[0].MetadataUpdatedAtUtc);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-family-profile-tests-" + Guid.NewGuid());
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
