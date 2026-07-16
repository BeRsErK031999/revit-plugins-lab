namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldPreviewLayout(
    IReadOnlyList<IsoFieldPreviewPolyline> Polylines,
    double Width,
    double Height)
{
    public static IsoFieldPreviewLayout Empty(double width, double height)
    {
        return new IsoFieldPreviewLayout(Array.Empty<IsoFieldPreviewPolyline>(), width, height);
    }
}

public sealed record IsoFieldPreviewPolyline(
    string Id,
    IReadOnlyList<IsoFieldPoint> Points,
    string? ZoneName = null,
    double? Confidence = null,
    IsoFieldLayerRole? LayerRole = null);
