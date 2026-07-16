namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRulePreviewResult(
    IReadOnlyList<RebarRulePreviewItem> Items,
    IReadOnlyList<string> Diagnostics,
    IsoFieldEngineeringSettings? EngineeringSettings = null,
    int EstimatedBarCount = 0,
    IReadOnlyList<string>? BaseDiagnostics = null)
{
    public IReadOnlyList<string> EffectiveBaseDiagnostics =>
        BaseDiagnostics ?? Diagnostics;

    public IReadOnlyList<RebarRulePreviewItem> ActiveItems =>
        Items.Where(item => item.IsIncluded).ToArray();

    public bool CanCreateRebar => ActiveItems.Count > 0
        && Diagnostics.Count == 0
        && ActiveItems.All(item => item.HasValidRule);

    public bool IsEngineeringPreview => EngineeringSettings is not null
        && ActiveItems.Count > 0
        && ActiveItems.All(item => item.Rule.IsEngineeringRule);
}
