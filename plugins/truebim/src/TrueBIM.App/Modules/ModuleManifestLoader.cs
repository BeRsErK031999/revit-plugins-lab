using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules;

public sealed class ModuleManifestLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ITrueBimLogger logger;

    public ModuleManifestLoader(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ModuleManifestLoadResult Load(string modulesRoot, string revitVersion)
    {
        Guard.NotNullOrWhiteSpace(modulesRoot, nameof(modulesRoot));
        Guard.NotNullOrWhiteSpace(revitVersion, nameof(revitVersion));

        logger.Info($"Loading TrueBIM module manifests from '{modulesRoot}' for Revit {revitVersion}.");

        if (!Directory.Exists(modulesRoot))
        {
            logger.Warning($"TrueBIM module manifests folder was not found: '{modulesRoot}'.");
            return new ModuleManifestLoadResult([], InvalidManifestCount: 0, ManifestRootExists: false);
        }

        string[] manifestPaths = Directory.GetFiles(modulesRoot, "module.json", SearchOption.AllDirectories);
        logger.Info($"Found {manifestPaths.Length} TrueBIM module manifest files.");

        List<ModuleManifest> manifests = new();
        int invalidManifestCount = 0;

        foreach (string manifestPath in manifestPaths)
        {
            try
            {
                ModuleManifest manifest = ParseManifest(File.ReadAllText(manifestPath));
                if (!SupportsRevitVersion(manifest, revitVersion))
                {
                    logger.Info($"Skipping module manifest '{manifestPath}' because it does not support Revit {revitVersion}.");
                    continue;
                }

                manifests.Add(manifest);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or IOException)
            {
                invalidManifestCount++;
                logger.Warning($"Skipping invalid module manifest '{manifestPath}': {exception.Message}");
            }
        }

        logger.Info($"Loaded {manifests.Count} module manifests. Invalid manifests skipped: {invalidManifestCount}.");
        return new ModuleManifestLoadResult(manifests, invalidManifestCount, ManifestRootExists: true);
    }

    public static ModuleManifest ParseManifest(string json)
    {
        Guard.NotNullOrWhiteSpace(json, nameof(json));

        ModuleManifestDto dto = JsonSerializer.Deserialize<ModuleManifestDto>(json, SerializerOptions)
            ?? throw new InvalidOperationException("Module manifest is empty.");

        if (string.IsNullOrWhiteSpace(dto.Id))
        {
            throw new InvalidOperationException("Module manifest id is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            throw new InvalidOperationException("Module manifest displayName is required.");
        }

        if (dto.RevitVersions is null || dto.RevitVersions.Count == 0)
        {
            throw new InvalidOperationException("Module manifest revitVersions is required.");
        }

        return new ModuleManifest(
            dto.Id,
            dto.DisplayName,
            dto.Description ?? string.Empty,
            dto.Version ?? string.Empty,
            dto.EnabledByDefault,
            dto.RevitVersions);
    }

    public static bool SupportsRevitVersion(ModuleManifest manifest, string revitVersion)
    {
        Guard.NotNull(manifest, nameof(manifest));
        Guard.NotNullOrWhiteSpace(revitVersion, nameof(revitVersion));

        return manifest.RevitVersions.Contains(revitVersion, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ModuleManifestDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("enabledByDefault")]
        public bool EnabledByDefault { get; init; }

        [JsonPropertyName("revitVersions")]
        public IReadOnlyList<string>? RevitVersions { get; init; }
    }
}
