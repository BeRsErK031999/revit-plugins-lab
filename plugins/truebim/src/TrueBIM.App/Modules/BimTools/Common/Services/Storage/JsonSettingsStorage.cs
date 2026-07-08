using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.Common.Services.Storage;

public sealed class JsonSettingsStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly ITrueBimLogger logger;

    public JsonSettingsStorage(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static string CreateDefaultSettingsPath(string settingsKey, string fileName = "settings.json")
    {
        Guard.NotNullOrWhiteSpace(settingsKey, nameof(settingsKey));
        Guard.NotNullOrWhiteSpace(fileName, nameof(fileName));

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrueBIM",
            "BimTools",
            SanitizePathSegment(settingsKey),
            fileName);
    }

    public T LoadOrDefault<T>(string settingsPath, Func<T> defaultFactory)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        Guard.NotNull(defaultFactory, nameof(defaultFactory));

        if (!File.Exists(settingsPath))
        {
            return defaultFactory();
        }

        try
        {
            string json = File.ReadAllText(settingsPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? defaultFactory();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Failed to read BIM tool settings '{settingsPath}': {exception.Message}");
            return defaultFactory();
        }
    }

    public void Save<T>(string settingsPath, T settings)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        string? directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(settingsPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string SanitizePathSegment(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = new(value
            .Trim()
            .Select(character => invalidChars.Contains(character) ? '_' : character)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized)
            ? "general"
            : sanitized;
    }
}
