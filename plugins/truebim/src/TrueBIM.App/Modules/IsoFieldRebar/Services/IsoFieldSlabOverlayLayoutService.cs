using TrueBIM.App.Modules.IsoFieldRebar.Models;

namespace TrueBIM.App.Modules.IsoFieldRebar.Services;

public sealed class IsoFieldSlabOverlayLayoutService
{
    private const double Padding = 16;

    public IsoFieldSlabOverlayLayout Build(
        IsoFieldSlabBindingAnalysis analysis,
        double width,
        double height,
        IReadOnlyList<IsoFieldSlabRebarSegment>? rebarSegments = null)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Preview size must be positive.");
        }

        HashSet<string> removedZoneIds = new(analysis.RemovedZoneIds, StringComparer.Ordinal);
        IsoFieldPolyline[] removedZones = analysis.MappedZones
            .Where(zone => removedZoneIds.Contains(zone.Id))
            .ToArray();
        IsoFieldPoint[] points = analysis.OuterBoundaryFeet
            .Concat(analysis.HoleBoundariesFeet.SelectMany(loop => loop))
            .Concat(analysis.ClippedZones.SelectMany(zone => zone.Regions)
                .SelectMany(region => region.OuterBoundaryFeet
                    .Concat(region.HoleBoundariesFeet.SelectMany(hole => hole))))
            .Concat(removedZones.SelectMany(zone => zone.Points))
            .Concat((rebarSegments ?? Array.Empty<IsoFieldSlabRebarSegment>())
                .SelectMany(segment => new[] { segment.StartFeet, segment.EndFeet }))
            .Concat(analysis.ControlPointsFeet)
            .ToArray();
        if (points.Length == 0)
        {
            return new IsoFieldSlabOverlayLayout(
                Array.Empty<IsoFieldPoint>(),
                Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
                Array.Empty<IsoFieldSlabOverlayRegion>(),
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

        IsoFieldSlabOverlayRegion[] zones = analysis.ClippedZones
            .SelectMany(zone => zone.Regions.Select(region => new IsoFieldSlabOverlayRegion(
                zone.SourceZoneId,
                zone.ZoneName,
                zone.Confidence,
                zone.LayerRole,
                region.OuterBoundaryFeet.Select(Map).ToArray(),
                region.HoleBoundariesFeet
                    .Select(hole => (IReadOnlyList<IsoFieldPoint>)hole.Select(Map).ToArray())
                    .ToArray(),
                zone.RetainedAreaRatio,
                zone.WasClipped)))
            .ToArray();
        IsoFieldSlabRebarSegment[] mappedRebarSegments = (rebarSegments
                ?? Array.Empty<IsoFieldSlabRebarSegment>())
            .Select(segment => segment with
            {
                StartFeet = Map(segment.StartFeet),
                EndFeet = Map(segment.EndFeet)
            })
            .ToArray();

        return new IsoFieldSlabOverlayLayout(
            analysis.OuterBoundaryFeet.Select(Map).ToArray(),
            analysis.HoleBoundariesFeet
                .Select(loop => (IReadOnlyList<IsoFieldPoint>)loop.Select(Map).ToArray())
                .ToArray(),
            zones,
            removedZones
                .Select(zone => new IsoFieldPreviewPolyline(
                    zone.Id,
                    zone.Points.Select(Map).ToArray(),
                    zone.ZoneName,
                    zone.Confidence,
                    zone.LayerRole))
                .ToArray(),
            analysis.ControlPointsFeet.Select(Map).ToArray(),
            width,
            height,
            mappedRebarSegments);
    }
}
