using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Modules.BimTools.AutoTags.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoTags;

public sealed class AutoTagProfileStorageTests
{
    [Fact]
    public void Normalize_ClampsPreviewLimitAndDropsInvalidTagType()
    {
        AutoTagProfile profile = AutoTagProfileStorage.Normalize(new AutoTagProfile
        {
            Name = "  ",
            OnlyUntagged = false,
            UseLeader = true,
            OffsetRightMm = 6200,
            OffsetUpMm = double.NaN,
            MaxPreviewCount = 9000,
            SelectedTagTypeId = -1,
            SelectedTagTypeIdsByCategory = new Dictionary<long, long>
            {
                [-2000011] = 101,
                [-2000014] = -1
            },
            SelectedCategoryIds = [42, 42, -2000011]
        });

        Assert.Equal("Активный вид", profile.Name);
        Assert.False(profile.OnlyUntagged);
        Assert.True(profile.UseLeader);
        Assert.Equal(5000, profile.OffsetRightMm);
        Assert.Equal(0, profile.OffsetUpMm);
        Assert.Equal(5000, profile.MaxPreviewCount);
        Assert.Null(profile.SelectedTagTypeId);
        Assert.Equal(101, profile.SelectedTagTypeIdsByCategory[-2000011]);
        Assert.False(profile.SelectedTagTypeIdsByCategory.ContainsKey(-2000014));
        Assert.Equal([42, -2000011], profile.SelectedCategoryIds);
    }

    [Fact]
    public void Save_RoundTripsNormalizedProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        AutoTagProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new AutoTagProfile
        {
            Name = "  План 1  ",
            OnlyUntagged = true,
            UseLeader = false,
            OffsetRightMm = 125.5,
            OffsetUpMm = -80,
            MaxPreviewCount = 10,
            SelectedTagTypeId = 123,
            SelectedTagTypeIdsByCategory = new Dictionary<long, long>
            {
                [-2000011] = 456
            },
            SelectedCategoryIds = [-2000011, -2000014]
        });

        AutoTagProfile loaded = storage.Load();

        Assert.Equal("План 1", loaded.Name);
        Assert.True(loaded.OnlyUntagged);
        Assert.False(loaded.UseLeader);
        Assert.Equal(125.5, loaded.OffsetRightMm);
        Assert.Equal(-80, loaded.OffsetUpMm);
        Assert.Equal(50, loaded.MaxPreviewCount);
        Assert.Equal(123, loaded.SelectedTagTypeId);
        Assert.Equal(456, loaded.SelectedTagTypeIdsByCategory[-2000011]);
        Assert.Equal([-2000011, -2000014], loaded.SelectedCategoryIds);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-auto-tags-tests-" + Guid.NewGuid());
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
