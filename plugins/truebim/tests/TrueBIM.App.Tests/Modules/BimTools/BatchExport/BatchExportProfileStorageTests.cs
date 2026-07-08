using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.BimTools.BatchExport.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.BatchExport;

public sealed class BatchExportProfileStorageTests
{
    [Fact]
    public void Load_ReturnsDefaultsWhenFileDoesNotExist()
    {
        using TempDirectory temp = new();
        BatchExportProfileStorage storage = new(Path.Combine(temp.Path, "settings.json"), new TestLogger());

        BatchExportProfile profile = storage.Load();

        Assert.Equal("Рабочая документация", profile.Name);
        Assert.Equal(BatchExportNamingService.DefaultTemplate, profile.FileNameTemplate);
        Assert.True(profile.ExportPdf);
        Assert.False(profile.ExportDwg);
    }

    [Fact]
    public void Save_RoundTripsNormalizedProfile()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "settings.json");
        BatchExportProfileStorage storage = new(settingsPath, new TestLogger());

        storage.Save(new BatchExportProfile
        {
            Name = "  РД  ",
            ExportFolder = @"  C:\Exports  ",
            FileNameTemplate = "  {SheetNumber}  ",
            ExportPdf = false,
            ExportDwg = true
        });

        BatchExportProfile profile = storage.Load();
        Assert.Equal("РД", profile.Name);
        Assert.Equal(@"C:\Exports", profile.ExportFolder);
        Assert.Equal("{SheetNumber}", profile.FileNameTemplate);
        Assert.False(profile.ExportPdf);
        Assert.True(profile.ExportDwg);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "truebim-tests-" + Guid.NewGuid());
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
