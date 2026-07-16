using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabOverlayLayoutService
{
    private const double Padding = 16;

    public IsoFieldSlabOverlayLayout Build(
        IsoFieldSlabBindingAnalysis analysis,
        double width,
        double height)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Preview size must be positive.");
        }

        IsoFieldPoint[] points = analysis.OuterBoundaryFeet
            .Concat(analysis.HoleBoundariesFeet.SelectMany(loop => loop))
            .Concat(analysis.MappedZones.SelectMany(zone => zone.Points))
            .Concat(analysis.ControlPointsFeet)
            .ToArray();
        if (points.Length == 0)
        {
            return new IsoFieldSlabOverlayLayout(
                Array.Empty<IsoFieldPoint>(),
                Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
                Array.Empty<IsoFieldPreviewPolyline>(),
                Array.Empty<IsoFieldPoint>(),
                width,
                height);
        }

        double minX = points.Min(point => point.X);
        double maxX = points.Max(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxY = points.Max(point => point.Y);
        double sourceWidth = Math.Max(maxX - minX, 1e-6);
        double sourceHeight = Math.Max(maxY - minY, 1e-6);
        double scale = Math.Min(
            Math.Max(width - (Padding * 2), 1) / sourceWidth,
            Math.Max(height - (Padding * 2), 1) / sourceHeight);
        double scaledWidth = sourceWidth * scale;
        double scaledHeight = sourceHeight * scale;
        double offsetX = ((width - scaledWidth) / 2) - (minX * scale);
        double offsetY = ((height - scaledHeight) / 2) + (maxY * scale);

        IsoFieldPoint Map(IsoFieldPoint point) => new(
            offsetX + (point.X * scale),
            offsetY - (point.Y * scale));

        return new IsoFieldSlabOverlayLayout(
            analysis.OuterBoundaryFeet.Select(Map).ToArray(),
            analysis.HoleBoundariesFeet
                .Select(loop => (IReadOnlyList<IsoFieldPoint>)loop.Select(Map).ToArray())
                .ToArray(),
            analysis.MappedZones
                .Select(zone => new IsoFieldPreviewPolyline(
                    zone.Id,
                    zone.Points.Select(Map).ToArray(),
                    zone.ZoneName,
                    zone.Confidence,
                    zone.LayerRole))
                .ToArray(),
            analysis.ControlPointsFeet.Select(Map).ToArray(),
            width,
            height);
    }
}
