using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldZoneCorrectionServiceTests
{
    [Fact]
    public void Apply_ExcludesZoneAndChangesLegendClassWithoutMutatingSource()
    {
        IsoFieldRecognitionResult source = CreateRecognitionResult(
            CreateZone("zone-a", IsoFieldLayerRole.As1X, bandIndex: 0, offsetX: 0, confidence: 0.92),
            CreateZone("zone-b", IsoFieldLayerRole.As1X, bandIndex: 0, offsetX: 20, confidence: 0.88));

        IsoFieldRecognitionResult result = new IsoFieldZoneCorrectionService().Apply(
            source,
            [
                new IsoFieldZoneCorrection("zone-a", IsIncluded: false, LegendBandIndex: 0),
                new IsoFieldZoneCorrection("zone-b", IsIncluded: true, LegendBandIndex: 1)
            ],
            Array.Empty<IsoFieldZoneMerge>());

        IsoFieldPolyline zone = Assert.Single(result.Polylines);
        Assert.Equal("zone-b", zone.Id);
        Assert.Equal(1, zone.LegendBandIndex);
        Assert.Equal("1,5–2,5 см²/м · #00FF00", zone.ZoneName);
        Assert.Equal(2, source.Polylines.Count);
        Assert.Equal(0, source.Polylines[1].LegendBandIndex);
        Assert.Contains(result.Diagnostics, message => message.Contains("исключено 1", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, message => message.Contains("изменён класс у 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_MergesSameLayerAndClassIntoClosedConvexHull()
    {
        IsoFieldRecognitionResult source = CreateRecognitionResult(
            CreateZone("zone-a", IsoFieldLayerRole.As2X, bandIndex: 1, offsetX: 0, confidence: 0.91),
            CreateZone("zone-b", IsoFieldLayerRole.As2X, bandIndex: 1, offsetX: 20, confidence: 0.84));

        IsoFieldRecognitionResult result = new IsoFieldZoneCorrectionService().Apply(
            source,
            source.Polylines
                .Select(zone => new IsoFieldZoneCorrection(zone.Id, true, zone.LegendBandIndex))
                .ToArray(),
            [new IsoFieldZoneMerge(["zone-a", "zone-b"])]);

        IsoFieldPolyline merged = Assert.Single(result.Polylines);
        Assert.Equal("manual-merge-001", merged.Id);
        Assert.Equal(IsoFieldLayerRole.As2X, merged.LayerRole);
        Assert.Equal(1, merged.LegendBandIndex);
        Assert.Equal(0.84, merged.Confidence);
        Assert.True(merged.Points.Count >= 5);
        Assert.Equal(merged.Points[0], merged.Points[merged.Points.Count - 1]);
        Assert.Contains(merged.Points, point => point.X == 0 && point.Y == 0);
        Assert.Contains(merged.Points, point => point.X == 30 && point.Y == 10);
        Assert.Contains(result.Diagnostics, message => message.Contains("объединено групп 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_RejectsMergeAcrossLayers()
    {
        IsoFieldRecognitionResult source = CreateRecognitionResult(
            CreateZone("zone-a", IsoFieldLayerRole.As1X, bandIndex: 0, offsetX: 0, confidence: 0.9),
            CreateZone("zone-b", IsoFieldLayerRole.As3Y, bandIndex: 0, offsetX: 20, confidence: 0.9));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new IsoFieldZoneCorrectionService().Apply(
                source,
                source.Polylines
                    .Select(zone => new IsoFieldZoneCorrection(zone.Id, true, zone.LegendBandIndex))
                    .ToArray(),
                [new IsoFieldZoneMerge(["zone-a", "zone-b"])]));

        Assert.Contains("одного расчётного слоя", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_RejectsMergeWithDifferentClasses()
    {
        IsoFieldRecognitionResult source = CreateRecognitionResult(
            CreateZone("zone-a", IsoFieldLayerRole.As4Y, bandIndex: 0, offsetX: 0, confidence: 0.9),
            CreateZone("zone-b", IsoFieldLayerRole.As4Y, bandIndex: 1, offsetX: 20, confidence: 0.9));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            new IsoFieldZoneCorrectionService().Apply(
                source,
                source.Polylines
                    .Select(zone => new IsoFieldZoneCorrection(zone.Id, true, zone.LegendBandIndex))
                    .ToArray(),
                [new IsoFieldZoneMerge(["zone-a", "zone-b"])]));

        Assert.Contains("одинаковый класс", exception.Message, StringComparison.Ordinal);
    }

    private static IsoFieldRecognitionResult CreateRecognitionResult(
        params IsoFieldPolyline[] polylines)
    {
        IsoFieldLegendBand[] bands =
        [
            new IsoFieldLegendBand(0, 255, 255, 0, 0, 0.5, MinimumValue: 0.5, MaximumValue: 1.5),
            new IsoFieldLegendBand(1, 0, 255, 0, 0.5, 1, MinimumValue: 1.5, MaximumValue: 2.5)
        ];
        IsoFieldLegend[] legends = Enum.GetValues<IsoFieldLayerRole>()
            .Select(role => new IsoFieldLegend(bands, 10, 0, 100, role))
            .ToArray();
        return new IsoFieldRecognitionResult(polylines, ["Исходная диагностика"], legends);
    }

    private static IsoFieldPolyline CreateZone(
        string id,
        IsoFieldLayerRole layerRole,
        int bandIndex,
        double offsetX,
        double confidence)
    {
        return new IsoFieldPolyline(
            id,
            [
                new IsoFieldPoint(offsetX, 0),
                new IsoFieldPoint(offsetX + 10, 0),
                new IsoFieldPoint(offsetX + 10, 10),
                new IsoFieldPoint(offsetX, 10),
                new IsoFieldPoint(offsetX, 0)
            ],
            bandIndex == 0 ? "0,5–1,5 см²/м · #FFFF00" : "1,5–2,5 см²/м · #00FF00",
            confidence,
            layerRole,
            bandIndex);
    }
}
