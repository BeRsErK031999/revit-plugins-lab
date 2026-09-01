using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;

public sealed class ScheduleRegisterSettingsStorage
{
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public static ScheduleRegisterSettingsStorage ForRevitVersion(
        string revitVersion,
        ITrueBimLogger logger)
    {
        return new ScheduleRegisterSettingsStorage(
            JsonSettingsStorage.CreateDefaultSettingsPath(
                "schedule-register",
                $"settings-{NormalizeVersion(revitVersion)}.json"),
            logger);
    }

    public ScheduleRegisterSettingsStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));
        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public ScheduleRegisterSettings Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, CreateDefault));
    }

    public void Save(ScheduleRegisterSettings settings)
    {
        storage.Save(settingsPath, Normalize(settings));
    }

    public static ScheduleRegisterSettings CreateDefault()
    {
        return new ScheduleRegisterSettings();
    }

    public static ScheduleRegisterSettings Normalize(ScheduleRegisterSettings? settings)
    {
        settings ??= CreateDefault();
        return new ScheduleRegisterSettings
        {
            FilterEnabled = settings.FilterEnabled,
            FilterParameterName = settings.FilterParameterName?.Trim() ?? string.Empty,
            ExcludedValue = string.IsNullOrWhiteSpace(settings.ExcludedValue)
                ? ScheduleRegisterConstants.DefaultExcludedValue
                : settings.ExcludedValue.Trim(),
            TemplateProjectPath = settings.TemplateProjectPath?.Trim() ?? string.Empty
        };
    }

    public static IReadOnlyList<string> Validate(ScheduleRegisterSettings settings)
    {
        settings ??= CreateDefault();
        List<string> issues = [];
        if (settings.FilterEnabled && string.IsNullOrWhiteSpace(settings.FilterParameterName))
        {
            issues.Add("Выберите параметр спецификации для фильтрации.");
        }

        if (settings.FilterEnabled && string.IsNullOrWhiteSpace(settings.ExcludedValue))
        {
            issues.Add("Укажите исключающее значение параметра.");
        }

        return issues;
    }

    private static string NormalizeVersion(string value)
    {
        string normalized = new(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "default" : normalized;
    }
}
