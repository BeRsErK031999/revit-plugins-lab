using TrueBIM.App.Modules.BimTools.DatumExtents.Models;
using TrueBIM.App.Modules.BimTools.DatumExtents.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.DatumExtents;

public sealed class DatumExtentProfileStorageTests
{
    [Fact]
    public void Normalize_DefaultsEmptySelectionsAndUnknownTarget()
    {
        DatumExtentProfile profile = DatumExtentProfileStorage.Normalize(new DatumExtentProfile
        {
            Name = "  ",
            TargetExtentType = "wrong",
            IncludeEnd0 = false,
            IncludeEnd1 = false,
            IncludeGrids = false,
            IncludeLevels = false
        });

        Assert.Equal("Активный вид", profile.Name);
        Assert.Equal(DatumExtentTargets.ViewSpecific, profile.TargetExtentType);
        Assert.True(profile.IncludeEnd0);
        Assert.True(profile.IncludeEnd1);
        Assert.True(profile.IncludeGrids);
        Assert.True(profile.IncludeLevels);
    }

    [Fact]
    public void Save_RoundTripsNormalizedProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        DatumExtentProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new DatumExtentProfile
        {
            Name = "  Фасады  ",
            TargetExtentType = DatumExtentTargets.Model,
            IncludeEnd0 = true,
            IncludeEnd1 = false,
            IncludeGrids = true,
            IncludeLevels = false,
            PropagateToViews = true
        });

        DatumExtentProfile loaded = storage.Load();

        Assert.Equal("Фасады", loaded.Name);
        Assert.Equal(DatumExtentTargets.Model, loaded.TargetExtentType);
        Assert.True(loaded.IncludeEnd0);
        Assert.False(loaded.IncludeEnd1);
        Assert.True(loaded.IncludeGrids);
        Assert.False(loaded.IncludeLevels);
        Assert.True(loaded.PropagateToViews);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-datum-extents-tests-" + Guid.NewGuid());
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
