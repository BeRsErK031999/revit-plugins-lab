using TrueBIM.App.Modules;
using TrueBIM.App.Services.Logging;
using Xunit;

namespace TrueBIM.App.Tests.Modules;

public sealed class ModuleManifestLoaderTests
{
    [Fact]
    public void ParseManifest_ReadsValidManifest()
    {
        ModuleManifest manifest = ModuleManifestLoader.ParseManifest(
            """
            {
              "id": "truebim.sheet-numbering",
              "displayName": "Sheet Numbering",
              "description": "Renumber sheets.",
              "version": "0.1.0",
              "enabledByDefault": true,
              "revitVersions": ["2025"]
            }
            """);

        Assert.Equal("truebim.sheet-numbering", manifest.Id);
        Assert.Equal("Sheet Numbering", manifest.DisplayName);
        Assert.True(manifest.EnabledByDefault);
        Assert.Equal(["2025"], manifest.RevitVersions);
    }

    [Fact]
    public void Load_SkipsInvalidManifestWithoutFailingFullLoad()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Valid"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "Invalid"));
        File.WriteAllText(Path.Combine(temp.Path, "Valid", "module.json"), ValidManifest("truebim.sheet-numbering", "2025"));
        File.WriteAllText(Path.Combine(temp.Path, "Invalid", "module.json"), "{ broken json");
        TestLogger logger = new();
        ModuleManifestLoader loader = new(logger);

        ModuleManifestLoadResult result = loader.Load(temp.Path, "2025");

        ModuleManifest manifest = Assert.Single(result.Manifests);
        Assert.Equal("truebim.sheet-numbering", manifest.Id);
        Assert.Equal(1, result.InvalidManifestCount);
        Assert.Contains(logger.Warnings, warning => warning.Contains("Skipping invalid module manifest", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_FiltersUnsupportedRevitVersions()
    {
        using TempDirectory temp = new();
        Directory.CreateDirectory(Path.Combine(temp.Path, "Supported"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "Unsupported"));
        File.WriteAllText(Path.Combine(temp.Path, "Supported", "module.json"), ValidManifest("supported", "2025"));
        File.WriteAllText(Path.Combine(temp.Path, "Unsupported", "module.json"), ValidManifest("unsupported", "2024"));

        ModuleManifestLoadResult result = new ModuleManifestLoader(new TestLogger()).Load(temp.Path, "2025");

        ModuleManifest manifest = Assert.Single(result.Manifests);
        Assert.Equal("supported", manifest.Id);
    }

    private static string ValidManifest(string id, string revitVersion)
    {
        return $$"""
            {
              "id": "{{id}}",
              "displayName": "Module",
              "description": "Description.",
              "version": "0.1.0",
              "enabledByDefault": true,
              "revitVersions": ["{{revitVersion}}"]
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
