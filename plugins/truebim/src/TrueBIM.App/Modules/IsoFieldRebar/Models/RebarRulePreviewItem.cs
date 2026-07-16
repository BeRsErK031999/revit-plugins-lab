namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRulePreviewItem(
    string ZoneId,
    string ZoneName,
    RebarRule Rule,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<IsoFieldPolygonRegion>? Regions = null,
    int EstimatedBarCount = 0,
    IReadOnlyList<string>? BaseDiagnostics = null,
    bool IsIncluded = true,
    bool IsManuallyOverridden = false)
{
    public bool HasValidRule => Diagnostics.Count == 0;

    public bool IsValid => !IsIncluded || HasValidRule;

    public IReadOnlyList<IsoFieldPolygonRegion> EffectiveRegions =>
        Regions ?? Array.Empty<IsoFieldPolygonRegion>();

    public IReadOnlyList<string> EffectiveBaseDiagnostics =>
        BaseDiagnostics ?? Diagnostics;

    public string DisplayName => !IsIncluded
        ? $"{ZoneName}: исключена из раскладки"
        : Rule.IsEngineeringRule
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
