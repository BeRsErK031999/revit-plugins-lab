namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldPolyline(
    string Id,
    IReadOnlyList<IsoFieldPoint> Points,
    string? ZoneName = null,
    double? Confidence = null,
    IsoFieldLayerRole? LayerRole = null);
