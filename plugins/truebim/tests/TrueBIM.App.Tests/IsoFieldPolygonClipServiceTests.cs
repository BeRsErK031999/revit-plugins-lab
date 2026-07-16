using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldPolygonClipServiceTests
{
    private readonly IsoFieldPolygonClipService service = new();

    [Fact]
    public void Clip_PreservesZoneInsideHost()
    {
        IsoFieldClippedZone result = service.Clip(
            CreateZone("inside", 2, 2, 8, 8),
            CreateLoop(0, 0, 10, 10),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>());

        IsoFieldPolygonRegion region = Assert.Single(result.Regions);
        Assert.Empty(region.HoleBoundariesFeet);
        Assert.Equal(36, result.OriginalAreaSquareFeet, 6);
        Assert.Equal(36, result.ClippedAreaSquareFeet, 6);
        Assert.Equal(1, result.RetainedAreaRatio, 6);
        Assert.False(result.WasClipped);
    }

    [Fact]
    public void Clip_TrimsZoneAtOuterBoundary()
    {
        IsoFieldClippedZone result = service.Clip(
            CreateZone("partial", 5, 2, 15, 8),
            CreateLoop(0, 0, 10, 10),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>());

        Assert.Single(result.Regions);
        Assert.Equal(60, result.OriginalAreaSquareFeet, 6);
        Assert.Equal(30, result.ClippedAreaSquareFeet, 6);
        Assert.Equal(0.5, result.RetainedAreaRatio, 6);
        Assert.True(result.WasClipped);
    }

    [Fact]
    public void Clip_SubtractsHoleFromZone()
    {
        IsoFieldClippedZone result = service.Clip(
            CreateZone("with-hole", 2, 2, 8, 8),
            CreateLoop(0, 0, 10, 10),
            [CreateLoop(4, 4, 6, 6)]);

        IsoFieldPolygonRegion region = Assert.Single(result.Regions);
        Assert.Single(region.HoleBoundariesFeet);
        Assert.Equal(36, result.OriginalAreaSquareFeet, 6);
        Assert.Equal(32, result.ClippedAreaSquareFeet, 6);
        Assert.Equal(32d / 36d, result.RetainedAreaRatio, 6);
        Assert.True(result.WasClipped);
    }

    [Fact]
    public void Clip_RemovesZoneEntirelyInsideHole()
    {
        IsoFieldClippedZone result = service.Clip(
            CreateZone("removed", 4.25, 4.25, 5.75, 5.75),
            CreateLoop(0, 0, 10, 10),
            [CreateLoop(4, 4, 6, 6)]);

        Assert.True(result.IsEmpty);
        Assert.Empty(result.Regions);
        Assert.Equal(0, result.ClippedAreaSquareFeet, 6);
        Assert.Equal(0, result.RetainedAreaRatio, 6);
    }

    [Fact]
    public void Clip_ReturnsSeparateRegionsForConcaveHost()
    {
        IReadOnlyList<IsoFieldPoint> concaveHost =
        [
            new IsoFieldPoint(0, 0),
            new IsoFieldPoint(10, 0),
            new IsoFieldPoint(10, 10),
            new IsoFieldPoint(7, 10),
            new IsoFieldPoint(7, 3),
            new IsoFieldPoint(3, 3),
            new IsoFieldPoint(3, 10),
            new IsoFieldPoint(0, 10),
            new IsoFieldPoint(0, 0)
        ];

        IsoFieldClippedZone result = service.Clip(
            CreateZone("split", 1, 5, 9, 8),
            concaveHost,
            Array.Empty<IReadOnlyList<IsoFieldPoint>>());

        Assert.Equal(2, result.Regions.Count);
        Assert.Equal(12, result.ClippedAreaSquareFeet, 6);
        Assert.True(result.WasClipped);
    }

    private static IsoFieldPolyline CreateZone(
        string id,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return new IsoFieldPolyline(
            id,
            CreateLoop(minX, minY, maxX, maxY),
            id,
            0.95,
            IsoFieldLayerRole.As1X);
    }

    private static IReadOnlyList<IsoFieldPoint> CreateLoop(
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return
        [
            new IsoFieldPoint(minX, minY),
            new IsoFieldPoint(maxX, minY),
            new IsoFieldPoint(maxX, maxY),
            new IsoFieldPoint(minX, maxY),
            new IsoFieldPoint(minX, minY)
        ];
    }
}
