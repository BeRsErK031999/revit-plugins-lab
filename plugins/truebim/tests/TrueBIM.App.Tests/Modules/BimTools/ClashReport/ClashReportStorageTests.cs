using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.ClashReport.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ClashReport;

public sealed class ClashReportStorageTests
{
    [Fact]
    public void NormalizeProfile_ClampsPaddingAndDefaultsName()
    {
        ClashReportProfile profile = ClashReportStorage.NormalizeProfile(new ClashReportProfile
        {
            Name = "  ",
            LastCsvPath = "  C:/tmp/clashes.csv  ",
            SectionBoxPaddingMm = double.NaN,
            MinimumOverlapMm = -5,
            ClashType = ClashType.Clearance,
            GroupingStrategy = ClashGroupingStrategy.LocationBucket,
            DefaultAssignee = "  BIM Lead  ",
            HighlightOnNavigate = false,
            ScanCurrentModel = true,
            ScanRvtLinks = false,
            ScanLinksAgainstEachOther = true
        });

        Assert.Equal("Координационная проверка", profile.Name);
        Assert.Equal("C:/tmp/clashes.csv", profile.LastCsvPath);
        Assert.Equal(1500, profile.SectionBoxPaddingMm);
        Assert.Equal(0, profile.MinimumOverlapMm);
        Assert.Equal(ClashType.Clearance, profile.ClashType);
        Assert.Equal(ClashGroupingStrategy.LocationBucket, profile.GroupingStrategy);
        Assert.Equal("BIM Lead", profile.DefaultAssignee);
        Assert.False(profile.HighlightOnNavigate);
        Assert.True(profile.ScanCurrentModel);
        Assert.False(profile.ScanRvtLinks);
        Assert.True(profile.ScanLinksAgainstEachOther);
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
            "Fixed",
            fingerprint: "CM-STABLE",
            assignedTo: "Architect");

        storage.SaveStates(
            "model-a",
            [item],
            new ClashReportProfile { Name = "Model A", SectionBoxPaddingMm = 500, HighlightOnNavigate = true });

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
            string.Empty,
            fingerprint: "CM-STABLE");
        loadedStorage.ApplyState("model-a", loadedItem);
        ClashReportProfile loadedProfile = loadedStorage.LoadProfile();

        Assert.Equal(ClashStatus.Resolved, loadedItem.Status);
        Assert.Equal("Fixed", loadedItem.Comment);
        Assert.Equal("Architect", loadedItem.AssignedTo);
        Assert.Equal("Model A", loadedProfile.Name);
        Assert.Equal(500, loadedProfile.SectionBoxPaddingMm);
        Assert.True(loadedProfile.ScanRvtLinks);
    }

    [Fact]
    public void ApplyState_FallsBackToLegacyClashId()
    {
        ClashReportSettings settings = new()
        {
            States =
            {
                ["model-a::C-01"] = new ClashStateRecord
                {
                    Status = ClashStatus.Approved,
                    Comment = "Legacy state",
                    AssignedTo = "Engineer"
                }
            }
        };
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        ClashReportStorage storage = new(settingsPath, new TestLogger());
        System.Text.Json.JsonSerializerOptions options = new() { WriteIndented = true };
        File.WriteAllText(settingsPath, System.Text.Json.JsonSerializer.Serialize(settings, options));

        ClashReportStorage loadedStorage = new(settingsPath, new TestLogger());
        ClashItem item = new(
            "C-01",
            "Clash 01",
            10,
            20,
            null,
            null,
            null,
            ClashStatus.Open,
            string.Empty,
            fingerprint: "CM-NEW");

        loadedStorage.ApplyState("model-a", item);

        Assert.Equal(ClashStatus.Approved, item.Status);
        Assert.Equal("Legacy state", item.Comment);
        Assert.Equal("Engineer", item.AssignedTo);
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
