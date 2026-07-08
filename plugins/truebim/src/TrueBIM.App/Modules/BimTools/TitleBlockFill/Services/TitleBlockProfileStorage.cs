using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public sealed class TitleBlockProfileStorage
{
    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public TitleBlockProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath("title-block-fill"), logger)
    {
    }

    public TitleBlockProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public TitleBlockProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, CreateDefaultProfile));
    }

    public void Save(TitleBlockProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static TitleBlockProfile CreateDefaultProfile()
    {
        return new TitleBlockProfile
        {
            Name = "Рабочая документация",
            Rules =
            [
                new TitleBlockParameterRule()
            ]
        };
    }

    public static TitleBlockProfile Normalize(TitleBlockProfile? profile)
    {
        profile ??= CreateDefaultProfile();
        List<TitleBlockParameterRule> rules = profile.Rules
            .Select(NormalizeRule)
            .ToList();
        if (rules.Count == 0)
        {
            rules.Add(new TitleBlockParameterRule());
        }

        return new TitleBlockProfile
        {
            Name = string.IsNullOrWhiteSpace(profile.Name) ? "Рабочая документация" : profile.Name.Trim(),
            Rules = rules
        };
    }

    private static TitleBlockParameterRule NormalizeRule(TitleBlockParameterRule? rule)
    {
        rule ??= new TitleBlockParameterRule();
        string target = TitleBlockRuleTargets.All.Contains(rule.Target)
            ? rule.Target
            : TitleBlockRuleTargets.Sheet;
        string source = TitleBlockValueSources.All.Contains(rule.Source)
            ? rule.Source
            : TitleBlockValueSources.StaticText;

        return new TitleBlockParameterRule
        {
            IsEnabled = rule.IsEnabled,
            Target = target,
            ParameterName = rule.ParameterName?.Trim() ?? string.Empty,
            Source = source,
            Value = rule.Value?.Trim() ?? string.Empty
        };
    }
}
