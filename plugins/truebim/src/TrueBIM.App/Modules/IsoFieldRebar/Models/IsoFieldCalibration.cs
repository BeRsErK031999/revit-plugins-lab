namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldCalibration(
    IsoFieldPoint ImageAnchor,
    double RevitAnchorXFeet,
    double RevitAnchorYFeet,
    double MillimetersPerPixel,
    bool InvertImageY)
{
    public static IsoFieldCalibration Default { get; } = new(
        new IsoFieldPoint(0, 0),
        0,
        0,
        10,
        true);
}
