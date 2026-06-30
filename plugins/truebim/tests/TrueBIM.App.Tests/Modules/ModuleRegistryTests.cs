using TrueBIM.App.Modules;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules;

public sealed class ModuleRegistryTests
{
    [Fact]
    public void Create_SkipsUnknownModuleIds()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Known"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "Unknown"));
        File.WriteAllText(Path.Combine(temp.Path, "Known", "module.json"), Manifest("truebim.sheet-numbering"));
        File.WriteAllText(Path.Combine(temp.Path, "Unknown", "module.json"), Manifest("truebim.unknown"));
        TestLogger logger = new();
        string settingsPath = Path.Combine(temp.Path, "module-settings.json");

        ModuleRegistry registry = ModuleRegistry.Create(
            temp.Path,
            settingsPath,
            "2025",
            new ModuleManifestLoader(logger),
            new ModuleSettingsService(settingsPath, logger),
            logger);

        ModuleRegistryEntry module = Assert.Single(registry.Modules);
        Assert.Equal("truebim.sheet-numbering", module.Id);
        Assert.Contains(logger.Warnings, warning => warning.Contains("no known runtime implementation", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_FallsBackToBuiltInModulesWhenManifestRootIsMissing()
    {
        using TempDirectory temp = new();
        string missingModulesRoot = Path.Combine(temp.Path, "MissingModules");
        string settingsPath = Path.Combine(temp.Path, "module-settings.json");
        TestLogger logger = new();

        ModuleRegistry registry = ModuleRegistry.Create(
            missingModulesRoot,
            settingsPath,
            "2025",
            new ModuleManifestLoader(logger),
            new ModuleSettingsService(settingsPath, logger),
            logger);

        Assert.Collection(
            registry.Modules.OrderBy(module => module.Id, StringComparer.Ordinal),
            module => Assert.Equal("truebim.schedule-column-collapse", module.Id),
            module => Assert.Equal("truebim.sheet-numbering", module.Id));
        Assert.Contains(logger.Warnings, warning => warning.Contains("Falling back", StringComparison.Ordinal));
    }

    private static string Manifest(string id)
    {
        return $$"""
            {
              "id": "{{id}}",
              "displayName": "Module",
              "description": "Description.",
              "version": "0.1.0",
              "enabledByDefault": true,
              "revitVersions": ["2025"]
            }
            """;
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
        public List<string> Warnings { get; } = new();

        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
            Warnings.Add(message);
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
