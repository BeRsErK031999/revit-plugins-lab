using System.Globalization;

namespace TrueBIM.App.Modules.Lintels.Models;

public sealed record LintelElevationAnnotationText(string UpperLine, string LowerLine)
{
    private const double MetersPerFoot = 0.3048;
    private const double ZeroRoundingToleranceMeters = 0.0005;

    public static LintelElevationAnnotationText Create(double elevationFeet)
    {
        if (double.IsNaN(elevationFeet) || double.IsInfinity(elevationFeet))
        {
            throw new ArgumentOutOfRangeException(
                nameof(elevationFeet),
                "Elevation must be a finite Revit length in feet.");
        }

        double elevationMeters = elevationFeet * MetersPerFoot;
        if (Math.Abs(elevationMeters) < ZeroRoundingToleranceMeters)
        {
            elevationMeters = 0;
        }

        string lowerLine = elevationMeters.ToString(
            "+0.000;-0.000;±0.000",
            CultureInfo.InvariantCulture);
        return new LintelElevationAnnotationText("отм.", lowerLine);
    }
}
