namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRulePreviewItem(
    string ZoneId,
    string ZoneName,
    RebarRule Rule,
    IReadOnlyList<string> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;

    public string DisplayName => $"{ZoneName}: {Rule.BarTypeName}, шаг {Rule.SpacingMillimeters:0} мм";
}
