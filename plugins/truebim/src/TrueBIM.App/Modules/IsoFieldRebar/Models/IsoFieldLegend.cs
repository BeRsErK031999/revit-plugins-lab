namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldLegend(
    IReadOnlyList<IsoFieldLegendBand> Bands,
    int PixelY,
    int PixelStartX,
    int PixelEndX,
    IsoFieldLayerRole? LayerRole = null,
    IReadOnlyList<IsoFieldLegendBoundary>? Boundaries = null)
{
    public IReadOnlyList<IsoFieldLegendBoundary> EffectiveBoundaries =>
        Boundaries ?? Array.Empty<IsoFieldLegendBoundary>();

    public bool HasNumericRanges => Bands.Count > 0
        && Bands.All(band => band.MinimumValue.HasValue && band.MaximumValue.HasValue);

    public bool HasReinforcementLabels => EffectiveBoundaries.Count == Bands.Count + 1
        && EffectiveBoundaries.All(boundary => !string.IsNullOrWhiteSpace(boundary.ReinforcementLabel));
}

public sealed record IsoFieldLegendBoundary(
    int Index,
    double Ratio,
    double? Value = null,
    string? ReinforcementLabel = null,
    double? LabelConfidence = null);

public sealed record IsoFieldLegendBand(
    int Index,
    byte Red,
    byte Green,
    byte Blue,
    double StartRatio,
    double EndRatio,
    string? Label = null,
    double? MinimumValue = null,
    double? MaximumValue = null)
{
    public string HexColor => $"#{Red:X2}{Green:X2}{Blue:X2}";
}
