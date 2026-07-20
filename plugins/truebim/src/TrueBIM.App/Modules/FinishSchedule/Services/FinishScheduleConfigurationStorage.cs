using System.IO;
using System.Text.Json;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishScheduleConfigurationStorage
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public void Export(string path, FinishScheduleSettings settings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configuration path must not be empty.", nameof(path));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        FinishScheduleConfigurationData data = new()
        {
            Version = CurrentVersion,
            WallsClassificationValue = settings.Walls.ClassificationValue,
            FloorsClassificationValue = settings.Floors.ClassificationValue,
            CeilingsClassificationValue = settings.Ceilings.ClassificationValue,
            DescriptionParameter = ParameterReferenceData.FromReference(settings.DescriptionParameter),
            RoomIdentifierMode = settings.RoomIdentifier.Mode,
            RoomIdentifierParameter = ParameterReferenceData.FromReference(settings.RoomIdentifier.CustomParameter),
            RoomListOutputParameter = ParameterReferenceData.FromReference(settings.RoomListOutputParameter),
            WallsDescriptionParameter = ParameterReferenceData.FromReference(settings.Walls.OutputDescriptionParameter),
            WallsAreaParameter = ParameterReferenceData.FromReference(settings.Walls.OutputAreaParameter),
            FloorsDescriptionParameter = ParameterReferenceData.FromReference(settings.Floors.OutputDescriptionParameter),
            FloorsAreaParameter = ParameterReferenceData.FromReference(settings.Floors.OutputAreaParameter),
            CeilingsDescriptionParameter = ParameterReferenceData.FromReference(settings.Ceilings.OutputDescriptionParameter),
            CeilingsAreaParameter = ParameterReferenceData.FromReference(settings.Ceilings.OutputAreaParameter)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, SerializerOptions));
    }

    public FinishScheduleSettings Import(string path, FinishScheduleSettings currentSettings)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Configuration path must not be empty.", nameof(path));
        }

        if (currentSettings is null)
        {
            throw new ArgumentNullException(nameof(currentSettings));
        }

        FinishScheduleConfigurationData data = JsonSerializer.Deserialize<FinishScheduleConfigurationData>(
            File.ReadAllText(path),
            SerializerOptions) ?? throw new InvalidDataException("Файл конфигурации пуст или имеет неверный формат.");
        if (data.Version != CurrentVersion)
        {
            throw new InvalidDataException(
                $"Версия конфигурации {data.Version} не поддерживается. Ожидается версия {CurrentVersion}.");
        }

        RoomIdentifierMode roomMode = Enum.IsDefined(typeof(RoomIdentifierMode), data.RoomIdentifierMode)
            ? data.RoomIdentifierMode
            : RoomIdentifierMode.Number;
        return currentSettings with
        {
            DescriptionParameter = data.DescriptionParameter?.ToReference(),
            RoomIdentifier = new RoomIdentifierSettings(
                roomMode,
                roomMode == RoomIdentifierMode.CustomParameter
                    ? data.RoomIdentifierParameter?.ToReference()
                    : null),
            RoomListOutputParameter = data.RoomListOutputParameter?.ToReference(),
            Walls = currentSettings.Walls with
            {
                ClassificationValue = Normalize(data.WallsClassificationValue),
                OutputDescriptionParameter = data.WallsDescriptionParameter?.ToReference(),
                OutputAreaParameter = data.WallsAreaParameter?.ToReference()
            },
            Floors = currentSettings.Floors with
            {
                ClassificationValue = Normalize(data.FloorsClassificationValue),
                OutputDescriptionParameter = data.FloorsDescriptionParameter?.ToReference(),
                OutputAreaParameter = data.FloorsAreaParameter?.ToReference()
            },
            Ceilings = currentSettings.Ceilings with
            {
                ClassificationValue = Normalize(data.CeilingsClassificationValue),
                OutputDescriptionParameter = data.CeilingsDescriptionParameter?.ToReference(),
                OutputAreaParameter = data.CeilingsAreaParameter?.ToReference()
            }
        };
    }

    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}

internal sealed class FinishScheduleConfigurationData
{
    public int Version { get; set; }

    public string? WallsClassificationValue { get; set; }

    public string? FloorsClassificationValue { get; set; }

    public string? CeilingsClassificationValue { get; set; }

    public ParameterReferenceData? DescriptionParameter { get; set; }

    public RoomIdentifierMode RoomIdentifierMode { get; set; }

    public ParameterReferenceData? RoomIdentifierParameter { get; set; }

    public ParameterReferenceData? RoomListOutputParameter { get; set; }

    public ParameterReferenceData? WallsDescriptionParameter { get; set; }

    public ParameterReferenceData? WallsAreaParameter { get; set; }

    public ParameterReferenceData? FloorsDescriptionParameter { get; set; }

    public ParameterReferenceData? FloorsAreaParameter { get; set; }

    public ParameterReferenceData? CeilingsDescriptionParameter { get; set; }

    public ParameterReferenceData? CeilingsAreaParameter { get; set; }
}
