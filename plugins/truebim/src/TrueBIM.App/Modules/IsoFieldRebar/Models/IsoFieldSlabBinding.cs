namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSlabBindingInput(
    IsoFieldPoint ImagePoint1,
    IsoFieldPoint ImagePoint2,
    IsoFieldPoint HostPoint1Feet,
    IsoFieldPoint HostPoint2Feet,
    bool MirrorImageY,
    IsoFieldPoint? ImagePoint3 = null,
    IsoFieldPoint? HostPoint3Feet = null);

public sealed record IsoFieldPlanarTransform(
    IsoFieldPoint ImageAnchor,
    IsoFieldPoint HostAnchorFeet,
    double FeetPerPixel,
    double RotationRadians,
    bool MirrorImageY)
{
    public double MillimetersPerPixel => FeetPerPixel * 304.8;

    public double RotationDegrees => RotationRadians * 180 / Math.PI;

    public IsoFieldPoint Map(IsoFieldPoint imagePoint)
    {
        double deltaX = imagePoint.X - ImageAnchor.X;
        double deltaY = imagePoint.Y - ImageAnchor.Y;
        if (MirrorImageY)
        {
            deltaY = -deltaY;
        }

        double cos = Math.Cos(RotationRadians);
        double sin = Math.Sin(RotationRadians);
        double rotatedX = (deltaX * cos) - (deltaY * sin);
        double rotatedY = (deltaX * sin) + (deltaY * cos);
        return new IsoFieldPoint(
            HostAnchorFeet.X + (rotatedX * FeetPerPixel),
            HostAnchorFeet.Y + (rotatedY * FeetPerPixel));
    }
}

public sealed record IsoFieldSlabBindingAnalysis(
    IsoFieldPlanarTransform Transform,
    IsoFieldHostGeometry HostGeometry,
    IReadOnlyList<IsoFieldPolyline> MappedZones,
    IReadOnlyList<IsoFieldClippedZone> ClippedZones,
    IReadOnlyList<IsoFieldPoint> OuterBoundaryFeet,
    IReadOnlyList<IReadOnlyList<IsoFieldPoint>> HoleBoundariesFeet,
    IReadOnlyList<IsoFieldPoint> ControlPointsFeet,
    IReadOnlyList<string> ClippedZoneIds,
    IReadOnlyList<string> RemovedZoneIds,
    IReadOnlyList<string> OutsideZoneIds,
    int OutsideZoneCount,
    double RetainedAreaRatio,
    double ThirdPointDeviationMillimeters,
    double ThirdPointToleranceMillimeters,
    bool IsThirdPointValid,
    IReadOnlyList<string> Diagnostics,
    bool CanProceed)
{
    public double InsideSampleRatio => RetainedAreaRatio;
}
