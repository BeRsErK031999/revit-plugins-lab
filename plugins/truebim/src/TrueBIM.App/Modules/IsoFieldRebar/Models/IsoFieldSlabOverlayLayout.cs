namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSlabOverlayLayout(
    IReadOnlyList<IsoFieldPoint> OuterBoundary,
    IReadOnlyList<IReadOnlyList<IsoFieldPoint>> HoleBoundaries,
    IReadOnlyList<IsoFieldPreviewPolyline> Zones,
    IReadOnlyList<IsoFieldPoint> ControlPoints,
    double Width,
    double Height);
