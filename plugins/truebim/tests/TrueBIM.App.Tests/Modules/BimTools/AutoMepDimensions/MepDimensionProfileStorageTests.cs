using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoMepDimensions;

public sealed class MepDimensionProfileStorageTests
{
    [Fact]
    public void Normalize_DefaultsToPipesAndClampsAngleTolerance()
    {
        MepDimensionProfile profile = MepDimensionProfileStorage.Normalize(new MepDimensionProfile
        {
            Name = "  ",
            IncludePipes = false,
            IncludeDucts = false,
            IncludeCableTrays = false,
            IncludeConduits = false,
            AllowElementReferenceFallback = false,
            AngleToleranceDegrees = 90
        });

        Assert.Equal("Активный план", profile.Name);
        Assert.True(profile.IncludePipes);
        Assert.False(profile.IncludeDucts);
        Assert.False(profile.IncludeCableTrays);
        Assert.False(profile.IncludeConduits);
        Assert.False(profile.AllowElementReferenceFallback);
        Assert.Equal(30, profile.AngleToleranceDegrees);
    }

    [Fact]
    public void Save_RoundTripsNormalizedProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        MepDimensionProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new MepDimensionProfile
        {
            Name = "  План MEP  ",
            IncludePipes = false,
            IncludeDucts = true,
            IncludeCableTrays = true,
            IncludeConduits = false,
            AllowElementReferenceFallback = true,
            AngleToleranceDegrees = double.NaN
        });

        MepDimensionProfile loaded = storage.Load();

        Assert.Equal("План MEP", loaded.Name);
        Assert.False(loaded.IncludePipes);
        Assert.True(loaded.IncludeDucts);
        Assert.True(loaded.IncludeCableTrays);
        Assert.False(loaded.IncludeConduits);
        Assert.True(loaded.AllowElementReferenceFallback);
        Assert.Equal(10, loaded.AngleToleranceDegrees);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-auto-mep-dimensions-tests-" + Guid.NewGuid());
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
