namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldPolygonRegion(
    IReadOnlyList<IsoFieldPoint> OuterBoundaryFeet,
    IReadOnlyList<IReadOnlyList<IsoFieldPoint>> HoleBoundariesFeet,
    double AreaSquareFeet);

public sealed record IsoFieldClippedZone(
    string SourceZoneId,
    string? ZoneName,
    double? Confidence,
    IsoFieldLayerRole? LayerRole,
    int? LegendBandIndex,
    IReadOnlyList<IsoFieldPolygonRegion> Regions,
    double OriginalAreaSquareFeet,
    double ClippedAreaSquareFeet)
{
    public bool IsEmpty => Regions.Count == 0 || ClippedAreaSquareFeet <= 1e-9;

    public double RetainedAreaRatio => OriginalAreaSquareFeet <= 1e-9
        ? 0
        : Math.Max(0, Math.Min(1, ClippedAreaSquareFeet / OriginalAreaSquareFeet));

    public bool WasClipped => !IsEmpty && RetainedAreaRatio < 0.999999;
}
