using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingLanesTests
{
    [Fact]
    public void OverlappingRowsSeparateButDistantZonesReuseTheNearestRow()
    {
        DetailingLanes lanes = new(5);
        Assert.Equal(0, lanes.Reserve(0, 100));
        Assert.Equal(1, lanes.Reserve(40, 60));
        Assert.Equal(1, lanes.Reserve(103, 120));
        Assert.Equal(0, lanes.Reserve(140, 130));
        Assert.Equal(2, lanes.Count);
    }

    [Fact]
    public void CoincidentIntervalsAndTouchingLabelsNeverShareARow()
    {
        DetailingLanes lanes = new(5);
        Assert.Equal(0, lanes.Reserve(0, 100));
        Assert.Equal(1, lanes.Reserve(100, 0));
        Assert.Equal(2, lanes.Reserve(105, 115));
    }
}
