namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldArrayRebarPlacement(
    string ZoneId,
    string ZoneName,
    RebarRule Rule,
    IsoFieldRebarComponent Component,
    IsoFieldRebarPoint3D FirstBarStart,
    IsoFieldRebarPoint3D FirstBarEnd,
    IsoFieldRebarPoint3D LastBarStart,
    IsoFieldRebarPoint3D Normal,
    int BarCount,
    string StableId,
    IReadOnlyList<string> SourceStableIds)
{
    private const double MillimetersPerFoot = 304.8;
    public double BarLengthFeet => Distance(FirstBarStart, FirstBarEnd);

    public double ArrayWidthFeet
    {
        get
        {
            double calculatedWidth = DistanceToLine(
                LastBarStart,
                FirstBarStart,
                FirstBarEnd);
            double minimumFamilyWidth = Component.SpacingMillimeters / MillimetersPerFoot;
            return BarCount <= 1
                ? Math.Max(calculatedWidth, minimumFamilyWidth)
                : calculatedWidth;
        }
    }

    private static double Distance(IsoFieldRebarPoint3D first, IsoFieldRebarPoint3D second)
    {
        return Math.Sqrt(
            Math.Pow(second.XFeet - first.XFeet, 2)
            + Math.Pow(second.YFeet - first.YFeet, 2)
            + Math.Pow(second.ZFeet - first.ZFeet, 2));
    }

    private static double DistanceToLine(
        IsoFieldRebarPoint3D point,
        IsoFieldRebarPoint3D lineStart,
        IsoFieldRebarPoint3D lineEnd)
    {
        double dx = lineEnd.XFeet - lineStart.XFeet;
        double dy = lineEnd.YFeet - lineStart.YFeet;
        double dz = lineEnd.ZFeet - lineStart.ZFeet;
        double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length <= 1e-12)
        {
            return 0;
        }

        double px = point.XFeet - lineStart.XFeet;
        double py = point.YFeet - lineStart.YFeet;
        double pz = point.ZFeet - lineStart.ZFeet;
        double crossX = (py * dz) - (pz * dy);
        double crossY = (pz * dx) - (px * dz);
        double crossZ = (px * dy) - (py * dx);
        return Math.Sqrt((crossX * crossX) + (crossY * crossY) + (crossZ * crossZ)) / length;
    }
}
