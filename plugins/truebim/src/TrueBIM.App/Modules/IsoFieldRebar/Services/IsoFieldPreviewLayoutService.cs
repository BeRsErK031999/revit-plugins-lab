using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldPreviewLayoutService
{
    private const double DefaultPadding = 16;

    public IsoFieldPreviewLayout Build(
        IsoFieldRecognitionResult result,
        double width,
        double height)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Preview size must be positive.");
        }

        if (result.Polylines.Count == 0)
        {
            return IsoFieldPreviewLayout.Empty(width, height);
        }

        IReadOnlyList<IsoFieldPoint> sourcePoints = result.Polylines
            .SelectMany(polyline => polyline.Points)
            .ToArray();
        if (sourcePoints.Count == 0)
        {
            return IsoFieldPreviewLayout.Empty(width, height);
        }

        double minX = sourcePoints.Min(point => point.X);
        double maxX = sourcePoints.Max(point => point.X);
        double minY = sourcePoints.Min(point => point.Y);
        double maxY = sourcePoints.Max(point => point.Y);
        double sourceWidth = Math.Max(maxX - minX, 1);
        double sourceHeight = Math.Max(maxY - minY, 1);
        double drawingWidth = Math.Max(width - DefaultPadding * 2, 1);
        double drawingHeight = Math.Max(height - DefaultPadding * 2, 1);
        double scale = Math.Min(drawingWidth / sourceWidth, drawingHeight / sourceHeight);
        double scaledWidth = sourceWidth * scale;
        double scaledHeight = sourceHeight * scale;
        double offsetX = (width - scaledWidth) / 2 - minX * scale;
        double offsetY = (height - scaledHeight) / 2 - minY * scale;

        IsoFieldPreviewPolyline[] polylines = result.Polylines
            .Select(polyline => new IsoFieldPreviewPolyline(
                polyline.Id,
                polyline.Points
                    .Select(point => new IsoFieldPoint(
                        offsetX + point.X * scale,
                        offsetY + point.Y * scale))
                    .ToArray(),
                polyline.ZoneName,
                polyline.Confidence))
            .ToArray();

        return new IsoFieldPreviewLayout(polylines, width, height);
    }
}
