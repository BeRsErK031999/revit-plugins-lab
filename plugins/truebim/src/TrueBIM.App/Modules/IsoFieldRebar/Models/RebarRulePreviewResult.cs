namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRulePreviewResult(
    IReadOnlyList<RebarRulePreviewItem> Items,
    IReadOnlyList<string> Diagnostics)
{
    public bool CanCreateRebar => Items.Count > 0
        && Diagnostics.Count == 0
        && Items.All(item => item.IsValid);
}
