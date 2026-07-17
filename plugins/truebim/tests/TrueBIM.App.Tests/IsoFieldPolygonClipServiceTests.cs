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

    [Fact]
    public void UnionRegions_JoinsTouchingRegionsWithoutConvexHullExpansion()
    {
        IsoFieldPolygonRegion left = CreateRegion(0, 0, 2, 4);
        IsoFieldPolygonRegion right = CreateRegion(2, 1, 5, 3);

        IsoFieldPolygonRegion result = Assert.Single(service.UnionRegions([left, right]));

        Assert.Equal(14, result.AreaSquareFeet, 6);
        Assert.Contains(result.OuterBoundaryFeet, point => point.X == 2 && point.Y == 4);
        Assert.Contains(result.OuterBoundaryFeet, point => point.X == 2 && point.Y == 3);
    }

    [Fact]
    public void UnionRegions_KeepsDisconnectedRegionsSeparate()
    {
        IReadOnlyList<IsoFieldPolygonRegion> result = service.UnionRegions(
            [CreateRegion(0, 0, 2, 2), CreateRegion(3, 0, 5, 2)]);

        Assert.Equal(2, result.Count);
        Assert.Equal(8, result.Sum(region => region.AreaSquareFeet), 6);
    }

    [Fact]
    public void UnionRegions_PreservesHoleNotCoveredByAdjacentRegion()
    {
        IsoFieldPolygonRegion withHole = new(
            CreateLoop(0, 0, 4, 4),
            [CreateLoop(1, 1, 2, 2)],
            15);

        IsoFieldPolygonRegion result = Assert.Single(service.UnionRegions(
            [withHole, CreateRegion(4, 0, 6, 4)]));

        Assert.Single(result.HoleBoundariesFeet);
        Assert.Equal(23, result.AreaSquareFeet, 6);
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

    private static IsoFieldPolygonRegion CreateRegion(
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        return new IsoFieldPolygonRegion(
            CreateLoop(minX, minY, maxX, maxY),
            Array.Empty<IReadOnlyList<IsoFieldPoint>>(),
            (maxX - minX) * (maxY - minY));
    }
}
