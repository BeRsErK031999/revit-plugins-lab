using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintPresetStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string storagePath;
    private readonly ITrueBimLogger logger;

    public PrintPresetStorage(string storagePath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(storagePath, nameof(storagePath));
        this.storagePath = storagePath;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool StorageFileExists => File.Exists(storagePath);

    public static string CreateStoragePath(string revitVersion)
    {
        Guard.NotNullOrWhiteSpace(revitVersion, nameof(revitVersion));

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "TrueBIM", revitVersion, "print-presets.json");
    }

    public PrintPresetStoreState Load()
    {
        if (!File.Exists(storagePath))
        {
            logger.Info($"Print preset file was not found. Defaults will be used: '{storagePath}'.");
            return new PrintPresetStoreState();
        }

        try
        {
            PrintPresetStoreState state = JsonSerializer.Deserialize<PrintPresetStoreState>(
                File.ReadAllText(storagePath),
                SerializerOptions) ?? new PrintPresetStoreState();

            return Normalize(state);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to read print presets '{storagePath}'. Defaults will be used: {exception.Message}");
            return new PrintPresetStoreState();
        }
    }

    public void Save(PrintPresetStoreState state)
    {
        Guard.NotNull(state, nameof(state));

        try
        {
            string? directory = Path.GetDirectoryName(storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            PrintPresetStoreState normalizedState = Normalize(state);
            File.WriteAllText(storagePath, JsonSerializer.Serialize(normalizedState, SerializerOptions));
            logger.Info($"Print presets saved: '{storagePath}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to save print presets '{storagePath}': {exception.Message}");
        }
    }

    public static PrintPresetStoreState Normalize(PrintPresetStoreState state)
    {
        Guard.NotNull(state, nameof(state));

        PrintPresetStoreState normalizedState = new()
        {
            LastSelectedPresetName = NormalizeOptionalText(state.LastSelectedPresetName)
        };

        HashSet<string> seenPresetNames = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (PrintPreset? preset in state.Presets ?? [])
        {
            if (preset is null)
            {
                continue;
            }

            PrintPreset normalizedPreset = NormalizePreset(preset);
            if (seenPresetNames.Add(normalizedPreset.Name))
            {
                normalizedState.Presets.Add(normalizedPreset);
            }
        }

        if (normalizedState.LastSelectedPresetName is not null
            && normalizedState.FindPreset(normalizedState.LastSelectedPresetName) is null)
        {
            normalizedState.LastSelectedPresetName = normalizedState.Presets.FirstOrDefault()?.Name;
        }

        return normalizedState;
    }

    public static PrintPreset NormalizePreset(PrintPreset preset)
    {
        Guard.NotNull(preset, nameof(preset));

        return new PrintPreset
        {
            Name = NormalizePresetName(preset.Name),
            Settings = PrintSettingsService.Normalize(preset.Settings ?? PrintSettingsService.DefaultSettings),
            DwgProfile = DwgExportProfileStorage.NormalizeProfile(preset.DwgProfile ?? new DwgExportProfile())
        };
    }

    public static string NormalizePresetName(string? presetName)
    {
        return string.IsNullOrWhiteSpace(presetName)
            ? PrintPreset.DefaultPresetName
            : presetName!.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value!.Trim();
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
