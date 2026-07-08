namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public sealed class TitleBlockParameterRule
{
    public bool IsEnabled { get; set; } = true;

    public string Target { get; set; } = TitleBlockRuleTargets.Sheet;

    public string ParameterName { get; set; } = string.Empty;

    public string Source { get; set; } = TitleBlockValueSources.StaticText;

    public string Value { get; set; } = string.Empty;
}
