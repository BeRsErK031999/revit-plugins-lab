using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingSupplement7Tests
{
    [Fact]
    public void TouchingPairsMeasureTheirLowerBarsAndKeepTheHundredMillimetrePitch()
    {
        var source = Enumerable.Range(0, 8).SelectMany(row => new[] {
            (Id: row * 2, X: 0.0, Y: 50.0 + row * 100, Diameter: 10.0),
            (Id: row * 2 + 1, X: 0.0, Y: 60.0 + row * 100, Diameter: 10.0) }).ToList();
        var groups = DetailingBarBundles.Create(source);
        Assert.Equal(8, groups.Count);
        Assert.All(groups, group => Assert.Equal(2, group.Count));
        Assert.Equal(Enumerable.Range(0, 8).Select(i => i * 2), groups.Select(g => g[0]));
        var plan = DetailingChainPlan.Create(groups.Select(g => new DetailingChainPoint(g[0], source[g[0]].Y, "pair", false)), true);
        Assert.Single(plan.Sections);
        Assert.True(plan.Sections[0].UseEqualityFormula);
        Assert.Equal(100, plan.Sections[0].PitchMm);
        Assert.Equal(new[] { 0, 4, 8, 12 }, groups.Where((_, i) => i % 2 == 0).Select(g => g[0]));
    }

    [Fact]
    public void DifferentFacesAndRegularFiftyMillimetrePitchStaySeparate()
    {
        var groups = DetailingBarBundles.Create(new[] { (0, 0.0, 0.0, 20.0), (1, 0.0, 50.0, 20.0),
            (2, 100.0, 5.0, 20.0), (3, 100.0, 55.0, 20.0) });
        Assert.Equal(4, groups.Count);
        Assert.All(groups, group => Assert.Single(group));
    }

    [Theory]
    [InlineData(34.9, 1)]
    [InlineData(35.1, 2)]
    public void UsesActualDiametersAndFiveMillimetreClearGap(double axisDistance, int groups) =>
        Assert.Equal(groups, DetailingBarBundles.Create(new[] { (0, 0.0, 0.0, 20.0), (1, 0.0, axisDistance, 40.0) }).Count);

    [Fact]
    public void ThreeTouchingBarsHaveOneLowerReferenceRegardlessOfInputOrder()
    {
        var groups = DetailingBarBundles.Create(new[] { (2, 0.0, 20.0, 10.0), (0, 0.0, 0.0, 10.0), (1, 0.0, 10.0, 10.0) });
        Assert.Equal(new[] { 0, 1, 2 }, Assert.Single(groups));
    }

    [Fact]
    public void UnknownDiameterDoesNotInventBundles() => Assert.Equal(2,
        DetailingBarBundles.Create(new[] { (0, 0.0, 0.0, 0.0), (1, 0.0, 10.0, 0.0) }).Count);

    [Fact]
    public void CropFitsInsideTheMaskAt310FromBaselineAndKeepsVisibleFreeEnds()
    {
        var result = DetailingBoundsPlan.Fit(new DetailingRect(-250, -280, 500, 3250),
            new DetailingRect(-450, -280, 700, 3450), 310, 10);
        Assert.Equal(-560, result.Left); Assert.Equal(810, result.Right);
        Assert.Equal(-590, result.Bottom); Assert.Equal(3560, result.Top);
        var extended = DetailingBoundsPlan.Fit(new DetailingRect(-250, -280, 500, 3250),
            new DetailingRect(-450, -620, 700, 3450), 310, 10);
        Assert.Equal(-630, extended.Bottom);
    }
}
