using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules;

public sealed class ModuleSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string settingsPath;
    private readonly ITrueBimLogger logger;
    private ModuleSettings settings;

    public ModuleSettingsService(string settingsPath, ITrueBimLogger logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        this.settingsPath = settingsPath;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        settings = LoadSettings(settingsPath, logger);
    }

    public bool IsEnabled(ModuleManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return settings.Modules.TryGetValue(manifest.Id, out bool enabled)
            ? enabled
            : manifest.EnabledByDefault;
    }

    public void SetEnabled(string moduleId, bool isEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        settings.Modules[moduleId] = isEnabled;
        Save();
        logger.Info($"Module enabled state changed: {moduleId} = {isEnabled}.");
    }

    private static ModuleSettings LoadSettings(string settingsPath, ITrueBimLogger logger)
    {
        if (!File.Exists(settingsPath))
        {
            logger.Info($"Module settings file was not found. Manifest enabledByDefault values will be used: '{settingsPath}'.");
            return new ModuleSettings(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            ModuleSettingsDto dto = JsonSerializer.Deserialize<ModuleSettingsDto>(
                File.ReadAllText(settingsPath),
                SerializerOptions) ?? new ModuleSettingsDto();

            return new ModuleSettings(new Dictionary<string, bool>(
                dto.Modules ?? new Dictionary<string, bool>(),
                StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.Warning($"Failed to read module settings '{settingsPath}'. Manifest defaults will be used: {exception.Message}");
            return new ModuleSettings(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private void Save()
    {
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        ModuleSettingsDto dto = new()
        {
            Modules = settings.Modules
        };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(dto, SerializerOptions));
    }

    private sealed record ModuleSettings(Dictionary<string, bool> Modules);

    private sealed record ModuleSettingsDto
    {
        [JsonPropertyName("modules")]
        public Dictionary<string, bool>? Modules { get; init; }
    }
}
