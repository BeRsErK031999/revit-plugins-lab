using TrueBIM.App.Modules;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules;

public sealed class ModuleSettingsServiceTests
{
    [Fact]
    public void IsEnabled_UsesEnabledByDefaultWhenSettingsFileDoesNotExist()
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), "truebim-tests-" + Guid.NewGuid(), "module-settings.json");
        ModuleSettingsService service = new(settingsPath, new TestLogger());

        Assert.True(service.IsEnabled(Manifest(enabledByDefault: true)));
        Assert.False(service.IsEnabled(Manifest(enabledByDefault: false)));
    }

    [Fact]
    public void IsEnabled_UsesUserSettingOverride()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "module-settings.json");
        ModuleSettingsService service = new(settingsPath, new TestLogger());

        service.SetEnabled("truebim.sheet-numbering", false);

        ModuleSettingsService reloadedService = new(settingsPath, new TestLogger());
        Assert.False(reloadedService.IsEnabled(Manifest(enabledByDefault: true)));
    }

    private static ModuleManifest Manifest(bool enabledByDefault)
    {
        return new ModuleManifest(
            "truebim.sheet-numbering",
            "Sheet Numbering",
            "Description.",
            "0.1.0",
            enabledByDefault,
            ["2025"]);
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
