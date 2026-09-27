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
    public IsoFieldPoint? ImageAxisPoint2 { get; init; }

    public IsoFieldPoint? ImageAxisPoint3 { get; init; }

    public IsoFieldPoint? HostAxisPoint2Feet { get; init; }

    public IsoFieldPoint? HostAxisPoint3Feet { get; init; }

    public double MillimetersPerPixel => FeetPerPixel * 304.8;

    public double SecondaryFeetPerPixel => UsesThreePointMapping
        ? Distance(HostAnchorFeet, HostAxisPoint3Feet!)
            / Distance(ImageAnchor, ApplyMirror(ImageAxisPoint3!))
        : FeetPerPixel;

    public double SecondaryMillimetersPerPixel => SecondaryFeetPerPixel * 304.8;

    public double RotationDegrees => RotationRadians * 180 / Math.PI;

    public bool UsesThreePointMapping => ImageAxisPoint2 is not null
        && ImageAxisPoint3 is not null
        && HostAxisPoint2Feet is not null
        && HostAxisPoint3Feet is not null;

    public double AxisScaleDifferencePercent
    {
        get
        {
            double maximumScale = Math.Max(FeetPerPixel, SecondaryFeetPerPixel);
            return maximumScale <= 0
                ? 0
                : Math.Abs(FeetPerPixel - SecondaryFeetPerPixel) / maximumScale * 100;
        }
    }

    public IsoFieldPoint Map(IsoFieldPoint imagePoint)
    {
        if (UsesThreePointMapping)
        {
            return MapByThreePoints(imagePoint);
        }

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

    private IsoFieldPoint MapByThreePoints(IsoFieldPoint imagePoint)
    {
        IsoFieldPoint axisPoint2 = ApplyMirror(ImageAxisPoint2!);
        IsoFieldPoint axisPoint3 = ApplyMirror(ImageAxisPoint3!);
        IsoFieldPoint mappedPoint = ApplyMirror(imagePoint);
        IsoFieldPoint mappedAnchor = ApplyMirror(ImageAnchor);
        double axis2X = axisPoint2.X - mappedAnchor.X;
        double axis2Y = axisPoint2.Y - mappedAnchor.Y;
        double axis3X = axisPoint3.X - mappedAnchor.X;
        double axis3Y = axisPoint3.Y - mappedAnchor.Y;
        double deltaX = mappedPoint.X - mappedAnchor.X;
        double deltaY = mappedPoint.Y - mappedAnchor.Y;
        double determinant = (axis2X * axis3Y) - (axis2Y * axis3X);
        double axis2Factor = ((deltaX * axis3Y) - (deltaY * axis3X)) / determinant;
        double axis3Factor = ((axis2X * deltaY) - (axis2Y * deltaX)) / determinant;
        double hostAxis2X = HostAxisPoint2Feet!.X - HostAnchorFeet.X;
        double hostAxis2Y = HostAxisPoint2Feet.Y - HostAnchorFeet.Y;
        double hostAxis3X = HostAxisPoint3Feet!.X - HostAnchorFeet.X;
        double hostAxis3Y = HostAxisPoint3Feet.Y - HostAnchorFeet.Y;
        return new IsoFieldPoint(
            HostAnchorFeet.X + (axis2Factor * hostAxis2X) + (axis3Factor * hostAxis3X),
            HostAnchorFeet.Y + (axis2Factor * hostAxis2Y) + (axis3Factor * hostAxis3Y));
    }

    private IsoFieldPoint ApplyMirror(IsoFieldPoint point)
    {
        return MirrorImageY
            ? new IsoFieldPoint(point.X, ImageAnchor.Y - (point.Y - ImageAnchor.Y))
            : point;
    }

    private static double Distance(IsoFieldPoint first, IsoFieldPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
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
    bool AreControlPointsInside,
    IReadOnlyList<string> Diagnostics,
    bool CanProceed)
{
    public double InsideSampleRatio => RetainedAreaRatio;
}
