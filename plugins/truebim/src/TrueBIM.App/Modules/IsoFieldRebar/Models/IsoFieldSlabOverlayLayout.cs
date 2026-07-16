namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSlabOverlayRegion(
    string SourceZoneId,
    string? ZoneName,
    double? Confidence,
    IsoFieldLayerRole? LayerRole,
    IReadOnlyList<IsoFieldPoint> OuterBoundary,
    IReadOnlyList<IReadOnlyList<IsoFieldPoint>> HoleBoundaries,
    double RetainedAreaRatio,
    bool WasClipped);

public sealed record IsoFieldSlabOverlayLayout(
    IReadOnlyList<IsoFieldPoint> OuterBoundary,
    IReadOnlyList<IReadOnlyList<IsoFieldPoint>> HoleBoundaries,
    IReadOnlyList<IsoFieldSlabOverlayRegion> Zones,
    IReadOnlyList<IsoFieldPreviewPolyline> RemovedZones,
    IReadOnlyList<IsoFieldPoint> ControlPoints,
    double Width,
    double Height,
    IReadOnlyList<IsoFieldSlabRebarSegment>? RebarSegments = null)
{
    public IReadOnlyList<IsoFieldSlabRebarSegment> EffectiveRebarSegments =>
        RebarSegments ?? Array.Empty<IsoFieldSlabRebarSegment>();
}
