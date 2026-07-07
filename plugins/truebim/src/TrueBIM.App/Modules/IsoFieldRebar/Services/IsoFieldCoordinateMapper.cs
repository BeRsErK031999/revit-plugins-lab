using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldCoordinateMapper
{
    private const double MillimetersPerFoot = 304.8;

    public IsoFieldPoint MapToRevitPlaneFeet(IsoFieldPoint imagePoint, IsoFieldCalibration calibration)
    {
        Validate(calibration);

        double feetPerPixel = calibration.MillimetersPerPixel / MillimetersPerFoot;
        double deltaX = imagePoint.X - calibration.ImageAnchor.X;
        double deltaY = imagePoint.Y - calibration.ImageAnchor.Y;

        return new IsoFieldPoint(
            calibration.RevitAnchorXFeet + (deltaX * feetPerPixel),
            calibration.RevitAnchorYFeet + ((calibration.InvertImageY ? -deltaY : deltaY) * feetPerPixel));
    }

    public void Validate(IsoFieldCalibration calibration)
    {
        if (calibration is null)
        {
            throw new ArgumentNullException(nameof(calibration));
        }

        if (!IsFinite(calibration.ImageAnchor.X) || !IsFinite(calibration.ImageAnchor.Y))
        {
            throw new InvalidOperationException("Координаты image anchor должны быть конечными числами.");
        }

        if (!IsFinite(calibration.RevitAnchorXFeet) || !IsFinite(calibration.RevitAnchorYFeet))
        {
            throw new InvalidOperationException("Координаты Revit anchor должны быть конечными числами.");
        }

        if (!IsFinite(calibration.MillimetersPerPixel) || calibration.MillimetersPerPixel <= 0)
        {
            throw new InvalidOperationException("Масштаб калибровки должен быть больше 0 мм/пикс.");
        }
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
