using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashReportStorageTests
{
    [Fact]
    public void NormalizeProfile_DefaultsNameAndTrimsImportPath()
    {
        ClashReportProfile profile = ClashReportStorage.NormalizeProfile(new ClashReportProfile
        {
            Name = "  ",
            LastImportPath = "  C:/tmp/clashes.xml  ",
            HighlightOnNavigate = false
        });

        Assert.Equal("Импорт коллизий", profile.Name);
        Assert.Equal("C:/tmp/clashes.xml", profile.LastImportPath);
        Assert.False(profile.HighlightOnNavigate);
    }

    [Fact]
    public void SaveStates_RoundTripsStatusAndCommentByModelAndClashId()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        ClashReportStorage storage = new(settingsPath, new TestLogger());
        ClashItem item = new(
            "C-01",
            "Clash 01",
            10,
            20,
            null,
            null,
            null,
            ClashStatus.Resolved,
            "Fixed");

        storage.SaveStates(
            "model-a",
            [item],
            new ClashReportProfile
            {
                Name = "Model A",
                LastImportPath = "C:/tmp/model-a.xml",
                HighlightOnNavigate = true
            });

        ClashReportStorage loadedStorage = new(settingsPath, new TestLogger());
        ClashItem loadedItem = new(
            "C-01",
            "Clash 01",
            10,
            20,
            null,
            null,
            null,
            ClashStatus.Open,
            string.Empty);
        loadedStorage.ApplyState("model-a", loadedItem);
        ClashReportProfile loadedProfile = loadedStorage.LoadProfile();

        Assert.Equal(ClashStatus.Resolved, loadedItem.Status);
        Assert.Equal("Fixed", loadedItem.Comment);
        Assert.Equal("Model A", loadedProfile.Name);
        Assert.Equal("C:/tmp/model-a.xml", loadedProfile.LastImportPath);
        Assert.True(loadedProfile.HighlightOnNavigate);
    }

    [Fact]
    public void LoadProfile_IgnoresLegacySectionBoxPadding()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        File.WriteAllText(
            settingsPath,
            """
            {
              "Profile": {
                "Name": "  Legacy import  ",
                "LastImportPath": "  C:/tmp/legacy.xml  ",
                "SectionBoxPaddingMm": 5000,
                "HighlightOnNavigate": false
              },
              "States": {}
            }
            """);

        ClashReportStorage storage = new(settingsPath, new TestLogger());
        ClashReportProfile profile = storage.LoadProfile();

        Assert.Equal("Legacy import", profile.Name);
        Assert.Equal("C:/tmp/legacy.xml", profile.LastImportPath);
        Assert.False(profile.HighlightOnNavigate);

        storage.SaveProfile(profile);
        Assert.DoesNotContain("SectionBoxPaddingMm", File.ReadAllText(settingsPath));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-clash-storage-tests-" + Guid.NewGuid());
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
