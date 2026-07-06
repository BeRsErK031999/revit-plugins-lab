using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class ParaManagerCategoryPresetService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string settingsPath;
    private readonly ITrueBimLogger logger;

    public ParaManagerCategoryPresetService(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        this.settingsPath = settingsPath;
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string GetDefaultSettingsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrueBIM",
            "ParaManager",
            "category-preset.json");
    }

    public IReadOnlyList<string> Load()
    {
        if (!File.Exists(settingsPath))
        {
            return [];
        }

        try
        {
            CategoryPresetDto dto = JsonSerializer.Deserialize<CategoryPresetDto>(
                File.ReadAllText(settingsPath),
                SerializerOptions) ?? new CategoryPresetDto();
            return Normalize(dto.Categories ?? []);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.Warning($"Failed to read ParaManager category preset '{settingsPath}': {exception.Message}");
            return [];
        }
    }

    public void Save(IEnumerable<string> categories)
    {
        IReadOnlyList<string> normalizedCategories = Normalize(categories);
        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        CategoryPresetDto dto = new()
        {
            Categories = normalizedCategories.ToList()
        };
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(dto, SerializerOptions));
        logger.Info($"Saved ParaManager category preset with {normalizedCategories.Count} categories.");
    }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> categories)
    {
        return categories
            .Select(category => category.Trim())
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private sealed record CategoryPresetDto
    {
        [JsonPropertyName("categories")]
        public List<string>? Categories { get; init; }
    }
}
