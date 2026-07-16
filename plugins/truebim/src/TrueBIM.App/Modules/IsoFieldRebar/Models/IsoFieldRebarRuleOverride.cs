namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarRuleOverride(
    string ZoneId,
    bool IsIncluded,
    string ReinforcementLabel);

public sealed record IsoFieldRebarRuleOverrideValidation(
    RebarRule? Rule,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0 && Rule is not null;
}
