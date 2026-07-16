namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRulePreviewResult(
    IReadOnlyList<RebarRulePreviewItem> Items,
    IReadOnlyList<string> Diagnostics,
    IsoFieldEngineeringSettings? EngineeringSettings = null,
    int EstimatedBarCount = 0)
{
    public bool CanCreateRebar => Items.Count > 0
        && Diagnostics.Count == 0
        && Items.All(item => item.IsValid);

    public bool IsEngineeringPreview => EngineeringSettings is not null
        && Items.Count > 0
        && Items.All(item => item.Rule.IsEngineeringRule);
}
