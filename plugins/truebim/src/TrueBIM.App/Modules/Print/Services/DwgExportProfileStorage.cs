using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class DwgExportProfileStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private readonly string storagePath;
    private readonly ITrueBimLogger logger;

    public DwgExportProfileStorage(string storagePath, ITrueBimLogger logger)
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
        return Path.Combine(appData, "TrueBIM", revitVersion, "dwg-export-profiles.json");
    }

    public DwgExportProfileStoreState Load()
    {
        if (!File.Exists(storagePath))
        {
            logger.Info($"DWG profile file was not found. Defaults will be used: '{storagePath}'.");
            return new DwgExportProfileStoreState();
        }

        try
        {
            DwgExportProfileStoreState state = JsonSerializer.Deserialize<DwgExportProfileStoreState>(
                File.ReadAllText(storagePath),
                SerializerOptions) ?? new DwgExportProfileStoreState();

            return Normalize(state);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to read DWG profiles '{storagePath}'. Defaults will be used: {exception.Message}");
            return new DwgExportProfileStoreState();
        }
    }

    public void Save(DwgExportProfileStoreState state)
    {
        Guard.NotNull(state, nameof(state));

        try
        {
            string? directory = Path.GetDirectoryName(storagePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DwgExportProfileStoreState normalizedState = Normalize(state);
            File.WriteAllText(storagePath, JsonSerializer.Serialize(normalizedState, SerializerOptions));
            logger.Info($"DWG profiles saved: '{storagePath}'.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to save DWG profiles '{storagePath}': {exception.Message}");
        }
    }

    public static DwgExportProfileStoreState Normalize(DwgExportProfileStoreState state)
    {
        Guard.NotNull(state, nameof(state));

        DwgExportProfileStoreState normalizedState = new()
        {
            LastSelectedProfileName = NormalizeOptionalText(state.LastSelectedProfileName),
            LastFolder = NormalizeOptionalText(state.LastFolder),
            LastNameMask = NormalizeOptionalText(state.LastNameMask),
            LastFormatSelection = NormalizeOptionalText(state.LastFormatSelection)
        };

        HashSet<string> seenProfileNames = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (DwgExportProfile? profile in state.Profiles)
        {
            if (profile is null)
            {
                continue;
            }

            DwgExportProfile normalizedProfile = NormalizeProfile(profile);
            if (seenProfileNames.Add(normalizedProfile.ProfileName))
            {
                normalizedState.Profiles.Add(normalizedProfile);
            }
        }

        if (normalizedState.LastSelectedProfileName is not null
            && normalizedState.FindProfile(normalizedState.LastSelectedProfileName) is null)
        {
            normalizedState.LastSelectedProfileName = normalizedState.Profiles.FirstOrDefault()?.ProfileName;
        }

        return normalizedState;
    }

    public static DwgExportProfile NormalizeProfile(DwgExportProfile profile)
    {
        Guard.NotNull(profile, nameof(profile));

        DwgExportProfile normalizedProfile = profile.Clone();
        normalizedProfile.ProfileName = NormalizeProfileName(normalizedProfile.ProfileName);
        normalizedProfile.SourceRevitSetupName = PrintCadExportSetupService.NormalizeSetupName(normalizedProfile.SourceRevitSetupName);
        normalizedProfile.LayerMapping = NormalizeOptionalText(normalizedProfile.LayerMapping);
        normalizedProfile.LinetypesFileName = NormalizeOptionalText(normalizedProfile.LinetypesFileName);
        normalizedProfile.HatchPatternsFileName = NormalizeOptionalText(normalizedProfile.HatchPatternsFileName);
        normalizedProfile.NonplotSuffix = NormalizeOptionalText(normalizedProfile.NonplotSuffix);
        return normalizedProfile;
    }

    public static string NormalizeProfileName(string? profileName)
    {
        return string.IsNullOrWhiteSpace(profileName)
            ? DwgExportProfile.DefaultProfileName
            : profileName!.Trim();
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
