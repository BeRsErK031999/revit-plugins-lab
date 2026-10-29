using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingSectionLayoutTests
{
    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 3)]
    public void LowerBreakFollowsVisibleConcreteSides(bool left, bool right, int expected) =>
        Assert.Equal(expected, DetailingSectionLayout.LowerBreakKind(left, right));

    [Theory]
    [InlineData(new double[] { 0, 20, -30 }, DetailingVariant.Straight)]
    [InlineData(new double[] { -300, -250 }, DetailingVariant.BentSameSide)]
    [InlineData(new double[] { 300, 250 }, DetailingVariant.BentSameSide)]
    [InlineData(new double[] { -300, 250 }, DetailingVariant.BentOppositeSides)]
    public void VariantDistinguishesStraightMirroredAndOpposingBends(double[] offsets, DetailingVariant expected) =>
        Assert.Equal(expected, DetailingSectionLayout.VariantForBends(offsets));

    [Theory]
    [InlineData(new double[] { 0, 0.03, 200, 400 }, false)]
    [InlineData(new double[] { 0, 120, 250, 370, 500, 620 }, true)]
    [InlineData(new double[] { 0, 200, 400, 550, 700 }, true)]
    [InlineData(new double[] { 0, 200 }, false)]
    public void CountedTagDetectsNonuniformPitchAcrossAllVisibleRows(double[] positions, bool expected) =>
        Assert.Equal(expected, DetailingSectionLayout.HasIrregularPitch(positions));
}
