using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Services;

public static class AutoTagPlacementOffset
{
    private const double FeetPerMillimeter = 1.0 / 304.8;
    private const double MaxOffsetMillimeters = 5000.0;
    private const double ZeroTolerance = 0.001;

    public static XYZ Apply(XYZ basePoint, XYZ rightDirection, XYZ upDirection, double rightMm, double upMm)
    {
        (double x, double y, double z) = ApplyCoordinates(
            basePoint.X,
            basePoint.Y,
            basePoint.Z,
            rightDirection.X,
            rightDirection.Y,
            rightDirection.Z,
            upDirection.X,
            upDirection.Y,
            upDirection.Z,
            rightMm,
            upMm);

        return new XYZ(x, y, z);
    }

    public static (double X, double Y, double Z) ApplyCoordinates(
        double baseX,
        double baseY,
        double baseZ,
        double rightX,
        double rightY,
        double rightZ,
        double upX,
        double upY,
        double upZ,
        double rightMm,
        double upMm)
    {
        (double x, double y, double z) right = NormalizeOrZero(rightX, rightY, rightZ);
        (double x, double y, double z) up = NormalizeOrZero(upX, upY, upZ);
        double rightFeet = NormalizeMillimeters(rightMm) * FeetPerMillimeter;
        double upFeet = NormalizeMillimeters(upMm) * FeetPerMillimeter;

        return (
            baseX + (right.x * rightFeet) + (up.x * upFeet),
            baseY + (right.y * rightFeet) + (up.y * upFeet),
            baseZ + (right.z * rightFeet) + (up.z * upFeet));
    }

    public static double NormalizeMillimeters(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) < ZeroTolerance)
        {
            return 0.0;
        }

        if (value < -MaxOffsetMillimeters)
        {
            return -MaxOffsetMillimeters;
        }

        return value > MaxOffsetMillimeters ? MaxOffsetMillimeters : value;
    }

    public static string FormatForReport(double rightMm, double upMm)
    {
        double normalizedRight = NormalizeMillimeters(rightMm);
        double normalizedUp = NormalizeMillimeters(upMm);
        if (normalizedRight == 0.0 && normalizedUp == 0.0)
        {
            return string.Empty;
        }

        return $"Смещение: X {normalizedRight:0.#} мм, Y {normalizedUp:0.#} мм.";
    }

    private static (double X, double Y, double Z) NormalizeOrZero(double x, double y, double z)
    {
        double length = Math.Sqrt((x * x) + (y * y) + (z * z));
        return double.IsNaN(length) || double.IsInfinity(length) || length < 1e-9
            ? (0.0, 0.0, 0.0)
            : (x / length, y / length, z / length);
    }
}
