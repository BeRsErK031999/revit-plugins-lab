using TrueBIM.App.Modules.BimTools.JoinCut.Services;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.JoinCut;

public sealed class JoinCutConfigurationStorageTests
{
    [Fact]
    public void Load_CreatesDefaultConfigurationWhenFileDoesNotExist()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "configurations.json");
        JoinCutConfigurationStorage storage = new(settingsPath, new TestLogger());

        JoinCutConfigurationLoadResult result = storage.Load();

        Assert.Null(result.WarningMessage);
        Assert.True(File.Exists(settingsPath));
        Assert.Equal(JoinCutConfigurationStorage.DefaultConfigurationId, result.State.SelectedConfigurationId);
        Assert.Contains(result.State.Configurations, configuration => configuration.IsDefault && configuration.Name == "Стандартная");
        Assert.Empty(result.State.Configurations[0].JoinRules);
        Assert.Empty(result.State.Configurations[0].CutRules);
    }

    [Fact]
    public void Save_RoundTripsUserConfiguration()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "configurations.json");
        JoinCutConfigurationStorage storage = new(settingsPath, new TestLogger());
        JoinCutConfigurationState state = storage.Load().State;
        var configuration = storage.CreateConfiguration("Рабочая");
        state.Configurations.Add(configuration);
        state.SelectedConfigurationId = configuration.Id;

        storage.Save(state);
        JoinCutConfigurationState reloaded = storage.Load().State;

        Assert.Equal(configuration.Id, reloaded.SelectedConfigurationId);
        Assert.Contains(reloaded.Configurations, item => item.Name == "Рабочая" && !item.IsDefault);
    }

    [Fact]
    public void Load_BacksUpCorruptJsonAndReturnsDefaultConfiguration()
    {
        using TempDirectory temp = new();
        string settingsPath = Path.Combine(temp.Path, "configurations.json");
        File.WriteAllText(settingsPath, "{broken json");
        JoinCutConfigurationStorage storage = new(settingsPath, new TestLogger());

        JoinCutConfigurationLoadResult result = storage.Load();

        Assert.NotNull(result.WarningMessage);
        Assert.Equal(JoinCutConfigurationStorage.DefaultConfigurationId, result.State.SelectedConfigurationId);
        Assert.Contains(Directory.EnumerateFiles(temp.Path), path => Path.GetFileName(path).StartsWith("configurations.backup-", StringComparison.Ordinal));
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
