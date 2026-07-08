using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewOrientationResolverTests
{
    [Fact]
    public void TryResolveWallFacingCoordinates_ReturnsPerpendicularWallNormal()
    {
        bool resolved = OpeningViewOrientationResolver.TryResolveWallFacingCoordinates(
            startX: 0,
            startY: 0,
            endX: 10,
            endY: 0,
            elementFacingX: 0,
            elementFacingY: 1,
            out double x,
            out double y);

        Assert.True(resolved);
        Assert.Equal(0, x, 6);
        Assert.Equal(1, y, 6);
    }

    [Fact]
    public void TryResolveWallFacingCoordinates_FlipsNormalTowardElementFacing()
    {
        bool resolved = OpeningViewOrientationResolver.TryResolveWallFacingCoordinates(
            startX: 0,
            startY: 0,
            endX: 10,
            endY: 0,
            elementFacingX: 0,
            elementFacingY: -1,
            out double x,
            out double y);

        Assert.True(resolved);
        Assert.Equal(0, x, 6);
        Assert.Equal(-1, y, 6);
    }

    [Fact]
    public void TryResolveWallFacingCoordinates_RejectsZeroLengthWall()
    {
        bool resolved = OpeningViewOrientationResolver.TryResolveWallFacingCoordinates(
            startX: 5,
            startY: 5,
            endX: 5,
            endY: 5,
            elementFacingX: 0,
            elementFacingY: 1,
            out _,
            out _);

        Assert.False(resolved);
    }
}
