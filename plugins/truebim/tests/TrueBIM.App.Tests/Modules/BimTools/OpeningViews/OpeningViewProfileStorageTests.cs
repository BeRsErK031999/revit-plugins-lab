using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewProfileStorageTests
{
    [Fact]
    public void Normalize_DefaultsToDoorsAndClampsNumericSettings()
    {
        OpeningViewProfile profile = OpeningViewProfileStorage.Normalize(new OpeningViewProfile
        {
            Name = "  ",
            IncludeDoors = false,
            IncludeWindows = false,
            IncludeCurtainWalls = false,
            ElevationViewTypeId = -1,
            ViewTemplateId = 0,
            Scale = 900,
            CropMarginMm = 9000,
            DepthMarginMm = double.NaN,
            OrientationSource = "Unknown",
            ViewNameTemplate = "  "
        });

        Assert.Equal("Активный план", profile.Name);
        Assert.True(profile.IncludeDoors);
        Assert.False(profile.IncludeWindows);
        Assert.False(profile.IncludeCurtainWalls);
        Assert.Null(profile.ElevationViewTypeId);
        Assert.Null(profile.ViewTemplateId);
        Assert.Equal(500, profile.Scale);
        Assert.Equal(5000, profile.CropMarginMm);
        Assert.Equal(0, profile.DepthMarginMm);
        Assert.Equal(OpeningViewOrientationSources.ElementFacing, profile.OrientationSource);
        Assert.Equal("BIM_Opening_{CategoryKey}_{ElementId}_{Family}_{Type}", profile.ViewNameTemplate);
    }

    [Fact]
    public void Save_RoundTripsNormalizedProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        OpeningViewProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new OpeningViewProfile
        {
            Name = "  Двери АР  ",
            IncludeDoors = true,
            IncludeWindows = true,
            IncludeCurtainWalls = true,
            ElevationViewTypeId = 42,
            ViewTemplateId = 84,
            Scale = 25,
            CropMarginMm = 750,
            DepthMarginMm = 1200,
            OrientationSource = OpeningViewOrientationSources.HostWall,
            ViewNameTemplate = "  BIM_{ElementId}  "
        });

        OpeningViewProfile loaded = storage.Load();

        Assert.Equal("Двери АР", loaded.Name);
        Assert.True(loaded.IncludeDoors);
        Assert.True(loaded.IncludeWindows);
        Assert.True(loaded.IncludeCurtainWalls);
        Assert.Equal(42, loaded.ElevationViewTypeId);
        Assert.Equal(84, loaded.ViewTemplateId);
        Assert.Equal(25, loaded.Scale);
        Assert.Equal(750, loaded.CropMarginMm);
        Assert.Equal(1200, loaded.DepthMarginMm);
        Assert.Equal(OpeningViewOrientationSources.HostWall, loaded.OrientationSource);
        Assert.Equal("BIM_{ElementId}", loaded.ViewNameTemplate);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-opening-views-tests-" + Guid.NewGuid());
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
