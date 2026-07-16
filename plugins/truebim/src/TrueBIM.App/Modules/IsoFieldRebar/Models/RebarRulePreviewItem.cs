namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRulePreviewItem(
    string ZoneId,
    string ZoneName,
    RebarRule Rule,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<IsoFieldPolygonRegion>? Regions = null,
    int EstimatedBarCount = 0)
{
    public bool IsValid => Diagnostics.Count == 0;

    public IReadOnlyList<IsoFieldPolygonRegion> EffectiveRegions =>
        Regions ?? Array.Empty<IsoFieldPolygonRegion>();

    public string DisplayName => Rule.IsEngineeringRule
        ? $"{ZoneName}: требуется {Rule.RequiredAreaSquareCentimetersPerMeter:0.###}, "
            + $"принято {Rule.ProvidedAreaSquareCentimetersPerMeter:0.###} см²/м · "
            + $"{Rule.ReinforcementLabel} · {Rule.PlacementDirection}/{FormatFace(Rule.Face)} · "
            + $"стержней {EstimatedBarCount}"
        : $"{ZoneName}: {Rule.BarTypeName}, шаг {Rule.SpacingMillimeters:0} мм, направление {Rule.PlacementDirection}";

    private static string FormatFace(IsoFieldRebarFace? face)
    {
        return face == IsoFieldRebarFace.Bottom ? "низ" : "верх";
    }
}
