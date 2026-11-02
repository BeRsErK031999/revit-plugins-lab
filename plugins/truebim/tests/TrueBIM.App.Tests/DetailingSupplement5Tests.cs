using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingSupplement5Tests
{
    [Fact]
    public void BreakContourClearsTheWallBy250AndTheStraightBarTop()
    {
        var result = DetailingBoundsPlan.Around(new DetailingRect(0, 0, 250, 3000), new DetailingRect(40, 50, 210, 3750), 250);
        Assert.Equal(-250, result.Left); Assert.Equal(500, result.Right);
        Assert.Equal(-250, result.Bottom); Assert.Equal(4000, result.Top);
    }

    [Fact]
    public void OpposingBendsRemainInsideTheBreakContour()
    {
        var result = DetailingBoundsPlan.Around(new DetailingRect(0, 0, 300, 3000), new DetailingRect(-600, 50, 900, 2940), 250);
        Assert.Equal(-850, result.Left); Assert.Equal(1150, result.Right); Assert.Equal(3250, result.Top);
    }

    [Fact]
    public void ExpandingForGraphicExtremesIsIdempotentAndNeverShrinksTheCrop()
    {
        var crop = new DetailingRect(-500, -400, 800, 4000);
        var graphics = new DetailingRect(-550, -450, 750, 4100);
        var result = DetailingBoundsPlan.Expand(crop, graphics, 30);
        Assert.Equal(-580, result.Left); Assert.Equal(-480, result.Bottom);
        Assert.Equal(800, result.Right); Assert.Equal(4130, result.Top);
        var repeated = DetailingBoundsPlan.Expand(result, graphics, 30);
        Assert.Equal(result.Left, repeated.Left); Assert.Equal(result.Bottom, repeated.Bottom);
        Assert.Equal(result.Right, repeated.Right); Assert.Equal(result.Top, repeated.Top);
    }
}
