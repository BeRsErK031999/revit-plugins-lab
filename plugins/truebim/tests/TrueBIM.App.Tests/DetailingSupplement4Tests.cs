using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingSupplement4Tests
{
    private static readonly DetailingRect Box = new(0, 0, 1000, 4000);

    [Fact]
    public void ConcreteOnLeftAndBottomBecomesOneCornerBreak()
    {
        var paths = DetailingPerimeter.Trace(Box, new[] { (0, 3500d, 4000d), (1, 0d, 250d) });
        Assert.Equal(new[] { (0d, 500d), (0d, 0d), (250d, 0d) }, Assert.Single(paths));
    }

    [Fact]
    public void FoundationAcrossBothCornersUsesThreeSegments()
    {
        var paths = DetailingPerimeter.Trace(Box, new[] { (2, 0d, 300d), (1, 0d, 1000d), (0, 3600d, 4000d) });
        Assert.Equal(new[] { (0d, 400d), (0d, 0d), (1000d, 0d), (1000d, 300d) }, Assert.Single(paths));
    }

    [Fact]
    public void TopLeftCornerWrapsWithoutSplittingTheSameConcrete()
    {
        var paths = DetailingPerimeter.Trace(Box, new[] { (3, 800d, 1000d), (0, 0d, 250d) });
        Assert.Equal(new[] { (200d, 4000d), (0d, 4000d), (0d, 3750d) }, Assert.Single(paths));
    }

    [Fact]
    public void OpeningsStayOpenAndOverlappingLinkedConcreteDoesNotDuplicateBreaks()
    {
        var paths = DetailingPerimeter.Trace(Box, new[] { (0, 100d, 400d), (0, 200d, 500d), (0, 800d, 900d), (0, 800d, 900d) });
        Assert.Equal(2, paths.Count);
        Assert.Equal(new[] { (0d, 3900d), (0d, 3500d) }, paths[0]);
        Assert.Equal(new[] { (0d, 3200d), (0d, 3100d) }, paths[1]);
    }

    [Fact]
    public void NoConcreteDoesNotInventABreak() => Assert.Empty(DetailingPerimeter.Trace(Box, Array.Empty<(int, double, double)>()));

    [Fact]
    public void PreferredLeftReferenceWinsDespiteFloatingPointNoiseInTheRow()
    {
        var plan = DetailingChainPlan.Create(new[] { new DetailingChainPoint(1, 200, "bar"),
            new DetailingChainPoint(0, 200.00000001, "bar"), new DetailingChainPoint(2, 400, "bar") });
        Assert.Equal(0, plan.Points[0].Id);
    }

    [Fact]
    public void RegularFieldStaysSeparateFromSixIrregularBarsOfTheSameMark()
    {
        var groups = DetailingBarGroups.Create(new[]
        {
            Rows(1, 0, 200, 400, 600, 800), Rows(2, 0, 200, 400, 600, 800),
            Rows(3, 930), Rows(4, 930), Rows(5, 1050), Rows(6, 1050), Rows(7, 1180), Rows(8, 1180)
        });
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 1, 2 }, groups[0]);
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8 }, groups[1]);
    }

    [Fact]
    public void DifferentRegularPitchesDoNotMerge()
    {
        var groups = DetailingBarGroups.Create(new[] { Rows(1, 0, 200, 400), Rows(2, 530, 660, 790) });
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void IrregularSetOnTheOtherFacesGridKeepsItsOwnCountedMark()
    {
        var groups = DetailingBarGroups.Create(new[] { Rows(1, 0, 200, 400, 600, 800, 1000, 1200),
            Rows(2, 0, 200, 600, 1000, 1200) });
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { 2 }, groups[1]);
    }

    [Fact]
    public void SingleBarCanExtendARegularArrayOnlyOnItsGrid()
    {
        var groups = DetailingBarGroups.Create(new[] { Rows(1, 0, 200, 400), Rows(2, 600), Rows(3, 750) });
        Assert.Equal(new[] { 1, 2 }, groups[0]);
        Assert.Equal(new[] { 3 }, groups[1]);
    }

    [Fact]
    public void DisconnectedFieldsWithTheSamePitchRetainSeparateMarks()
    {
        Assert.Equal(2, DetailingBarGroups.Create(new[] { Rows(1, 0, 200, 400), Rows(2, 1000, 1200, 1400) }).Count);
    }

    [Theory]
    [InlineData(250, 200)]
    [InlineData(300, 250)]
    public void PinUsesConfirmedFiftyMillimetreDeduction(double thickness, double expected) =>
        Assert.Equal(expected, DetailingSectionLayout.PinLengthMm(thickness));

    private static (int Id, IReadOnlyList<double> Rows) Rows(int id, params double[] positions) => (id, positions);
}
