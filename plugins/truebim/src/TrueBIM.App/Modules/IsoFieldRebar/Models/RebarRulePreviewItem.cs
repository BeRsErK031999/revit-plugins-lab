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
    bool IsManuallyOverridden = false,
    IReadOnlyList<string>? SourceZoneIds = null)
{
    public bool HasValidRule => Diagnostics.Count == 0;

    public bool IsValid => !IsIncluded || HasValidRule;

    public IReadOnlyList<IsoFieldPolygonRegion> EffectiveRegions =>
        Regions ?? Array.Empty<IsoFieldPolygonRegion>();

    public IReadOnlyList<string> EffectiveBaseDiagnostics =>
        BaseDiagnostics ?? Diagnostics;

    public IReadOnlyList<string> EffectiveSourceZoneIds =>
        SourceZoneIds ?? [ZoneId];

    public bool IsMerged => EffectiveSourceZoneIds.Count > 1;

    public string DisplayName => !IsIncluded
        ? $"{ZoneName}: исключена из раскладки"
        : Rule.IsEngineeringRule
        ? $"{ZoneName}: требуется {Rule.RequiredAreaSquareCentimetersPerMeter:0.###}, "
            + $"принято {Rule.ProvidedAreaSquareCentimetersPerMeter:0.###} см²/м · "
            + $"{FormatReinforcement(Rule)} · {FormatDirection(Rule.PlacementDirection)} · {FormatFace(Rule.HostKind, Rule.Face)} · "
            + $"стержней {EstimatedBarCount}"
        : $"{ZoneName}: {Rule.BarTypeName}, шаг {Rule.SpacingMillimeters:0} мм, {FormatDirection(Rule.PlacementDirection)}";

    private static string FormatReinforcement(RebarRule rule)
    {
        return rule.EffectiveComponents.Count > 0
            ? string.Join(" + ", rule.EffectiveComponents.Select(component => component.UserDisplayName))
            : rule.ReinforcementLabel ?? "арматура не определена";
    }

    private static string FormatDirection(string direction)
    {
        return direction switch
        {
            "X" => "направление X",
            "Y" => "направление Y",
            "AlongHost" => "вдоль конструкции",
            _ => "направление выбрано автоматически"
        };
    }

    private static string FormatFace(string hostKind, IsoFieldRebarFace? face)
    {
        if (string.Equals(hostKind, "Wall", StringComparison.Ordinal))
        {
            return face == IsoFieldRebarFace.Bottom ? "внутренняя" : "наружная";
        }

        return face == IsoFieldRebarFace.Bottom ? "низ" : "верх";
    }
}
