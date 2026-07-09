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
            LibraryFiles =
            [
                new FamilyLibraryFile { Path = @" C:\Lib\Doors\Door.rfa ", IsEnabled = true },
                new FamilyLibraryFile { Path = @"C:\Lib\Doors\Door.rfa", IsEnabled = false },
                new FamilyLibraryFile { Path = @"C:\Lib\Doors\Door.txt", IsEnabled = true }
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
                    TypeCatalogPath = @" C:\Lib\Doors\Door.txt ",
                    TypeCatalogTypeNames = ["1000x2100", "900x2100", "1000x2100", " "],
                    CachedTypes =
                    [
                        new FamilyTypeInfo(
                            0,
                            "  900x2100  ",
                            [
                                new FamilyTypeParameterInfo("  Width  ", " 900 ", "Double", "Тип", string.Empty),
                                new FamilyTypeParameterInfo("Width", "900", "Double", "Тип", string.Empty),
                                new FamilyTypeParameterInfo("Height", "2100", "Double", "Тип", "BaseHeight")
                            ]),
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
        Assert.Single(normalized.LibraryFiles);
        Assert.Single(normalized.FavoritePaths);
        Assert.Single(normalized.CachedFiles);
        Assert.True(normalized.CachedFiles[0].IsFavorite);
        Assert.Equal(@"C:\Lib\Doors\Door.txt", normalized.CachedFiles[0].TypeCatalogPath);
        Assert.Equal(["1000x2100", "900x2100"], normalized.CachedFiles[0].TypeCatalogTypeNames);
        Assert.Equal(["1000x2100", "900x2100"], normalized.CachedFiles[0].CachedTypes.Select(type => type.Name));
        FamilyTypeInfo cachedType = normalized.CachedFiles[0].CachedTypes.Single(type => type.Name == "900x2100");
        Assert.Equal(["Height", "Width"], cachedType.Parameters.Select(parameter => parameter.Name));
        Assert.Equal("BaseHeight", cachedType.Parameters.Single(parameter => parameter.Name == "Height").Formula);
        Assert.NotNull(normalized.CachedFiles[0].MetadataUpdatedAtUtc);
    }

    [Fact]
    public void Save_RoundTripsProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        string thumbnailPath = Path.Combine(temp.Path, "thumbnails", "chair.png");
        DateTimeOffset thumbnailUpdatedAtUtc = new(2026, 7, 8, 7, 30, 0, TimeSpan.Zero);
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
                    ThumbnailPath = thumbnailPath,
                    ThumbnailUpdatedAtUtc = thumbnailUpdatedAtUtc,
                    TypeCatalogPath = Path.Combine(temp.Path, "Chair.txt"),
                    TypeCatalogTypeNames = ["Default", "Large"],
                    CachedTypes =
                    [
                        new FamilyTypeInfo(
                            0,
                            "Default",
                            [new FamilyTypeParameterInfo("Manufacturer", "TrueBIM", "String", "Тип", string.Empty)])
                    ],
                    IsFavorite = true
                }
            ]
        });

        FamilyManagerProfile loaded = storage.Load();

        Assert.Single(loaded.LibraryFolders);
        Assert.Single(loaded.CachedFiles);
        Assert.Single(loaded.FavoritePaths);
        Assert.Equal("Chair", loaded.CachedFiles[0].Name);
        Assert.Equal(thumbnailPath, loaded.CachedFiles[0].ThumbnailPath);
        Assert.Equal(thumbnailUpdatedAtUtc, loaded.CachedFiles[0].ThumbnailUpdatedAtUtc);
        Assert.Equal(Path.Combine(temp.Path, "Chair.txt"), loaded.CachedFiles[0].TypeCatalogPath);
        Assert.Equal(["Default", "Large"], loaded.CachedFiles[0].TypeCatalogTypeNames);
        FamilyTypeInfo loadedType = Assert.Single(loaded.CachedFiles[0].CachedTypes);
        Assert.Equal("Default", loadedType.Name);
        Assert.Equal("Manufacturer", Assert.Single(loadedType.Parameters).Name);
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
