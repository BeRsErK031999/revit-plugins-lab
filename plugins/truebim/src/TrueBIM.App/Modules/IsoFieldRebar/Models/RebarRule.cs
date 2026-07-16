namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRule(
    string Name,
    string HostKind,
    string BarTypeName,
    double SpacingMillimeters,
    string? Note = null,
    string PlacementDirection = "Auto",
    double? RequiredAreaSquareCentimetersPerMeter = null,
    double? ProvidedAreaSquareCentimetersPerMeter = null,
    string? ReinforcementLabel = null,
    IsoFieldLayerRole? LayerRole = null,
    IsoFieldRebarFace? Face = null,
    IReadOnlyList<IsoFieldRebarComponent>? Components = null,
    IsoFieldReinforcementMode? ReinforcementMode = null)
{
    public IReadOnlyList<IsoFieldRebarComponent> EffectiveComponents =>
        Components ?? Array.Empty<IsoFieldRebarComponent>();

    public bool IsEngineeringRule => RequiredAreaSquareCentimetersPerMeter.HasValue
        && ProvidedAreaSquareCentimetersPerMeter.HasValue
        && LayerRole.HasValue
        && Face is IsoFieldRebarFace.Bottom or IsoFieldRebarFace.Top
        && EffectiveComponents.Count > 0;
}
