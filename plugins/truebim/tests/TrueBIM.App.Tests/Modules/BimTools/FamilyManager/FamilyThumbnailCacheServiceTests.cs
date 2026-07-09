using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyThumbnailCacheServiceTests
{
    [Fact]
    public void BuildThumbnailPath_UsesFamilyFileFingerprint()
    {
        using TempDirectory temp = new();
        FamilyThumbnailCacheService service = new(temp.Path);
        FamilyFileItem family = new()
        {
            FilePath = @"C:\Library\Doors\Door.rfa",
            SizeBytes = 1024,
            LastWriteTimeUtc = new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero)
        };

        string first = service.BuildThumbnailPath(family);
        string second = service.BuildThumbnailPath(family);
        family.LastWriteTimeUtc = family.LastWriteTimeUtc.AddMinutes(1);
        string changed = service.BuildThumbnailPath(family);

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
        Assert.Equal(temp.Path, Path.GetDirectoryName(first));
        Assert.Equal(".png", Path.GetExtension(first));
    }

    [Fact]
    public void TryGetCachedThumbnail_RejectsStaleFamilyCache()
    {
        using TempDirectory temp = new();
        FamilyThumbnailCacheService service = new(temp.Path);
        FamilyFileItem family = new()
        {
            FilePath = @"C:\Library\Doors\Door.rfa",
            SizeBytes = 1024,
            LastWriteTimeUtc = new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero)
        };
        Directory.CreateDirectory(temp.Path);
        string currentThumbnail = service.BuildThumbnailPath(family);
        File.WriteAllText(currentThumbnail, "png");

        Assert.Equal(currentThumbnail, service.TryGetCachedThumbnail(family));

        family.ThumbnailPath = currentThumbnail;
        family.ThumbnailUpdatedAtUtc = DateTimeOffset.UtcNow;
        family.LastWriteTimeUtc = family.LastWriteTimeUtc.AddMinutes(1);

        Assert.Null(service.TryGetCachedThumbnail(family));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-family-thumbnail-tests-" + Guid.NewGuid());
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
