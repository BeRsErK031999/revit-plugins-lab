using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingSupplement6Tests
{
    [Theory]
    [InlineData(180, 160)]
    [InlineData(200, 160)]
    [InlineData(210, 160)]
    [InlineData(250, 200)]
    [InlineData(300, 250)]
    public void PinNeverFallsBelow160Millimetres(double wall, double expected) =>
        Assert.Equal(expected, DetailingSectionLayout.PinLengthMm(wall));

    [Fact]
    public void LowerCoversTouchOuterWitnessLinesAndMiddleTextUsesAnotherRow()
    {
        // Model millimetres at 1:50: 55 + 140 + 55, actual text widths 120/180.
        var plan = DetailingDimensionTextLayout.Place(new[] {
            new DetailingDimensionLabel(27.5, 55, 120), new DetailingDimensionLabel(125, 140, 180),
            new DetailingDimensionLabel(222.5, 55, 120) }, Array.Empty<(double, double)>(), 7.5, 7.5);
        Assert.Equal(-7.5, plan[0].Center + 60);
        Assert.Equal(257.5, plan[2].Center - 60);
        Assert.Equal(0, plan[0].Row); Assert.Equal(0, plan[2].Row);
        Assert.Equal(1, plan[1].Row);
    }

    [Fact]
    public void ConcreteDimensionThatFitsStaysCentered()
    {
        var plan = DetailingDimensionTextLayout.Place(new[] { new DetailingDimensionLabel(125, 250, 180) },
            Array.Empty<(double, double)>(), 7.5, 7.5);
        Assert.Equal(125, plan[0].Center); Assert.Equal(0, plan[0].Row);
    }

    [Fact]
    public void VerticalEndCoversMoveOutwardsWithoutMovingFittingMiddleDimension()
    {
        var plan = DetailingDimensionTextLayout.Place(new[] {
            new DetailingDimensionLabel(25, 50, 120), new DetailingDimensionLabel(175, 250, 180),
            new DetailingDimensionLabel(325, 50, 120) }, Array.Empty<(double, double)>(), 7.5, 32.5);
        Assert.Equal(-32.5, plan[0].Center + 60);
        Assert.Equal(175, plan[1].Center); Assert.Equal(0, plan[1].Row);
        Assert.Equal(382.5, plan[2].Center - 60);
    }

    [Fact]
    public void CrampedInteriorTextAvoidsFormulaWithoutDisplacingOtherValues()
    {
        var plan = DetailingDimensionTextLayout.Place(new[] {
            new DetailingDimensionLabel(100, 200, 100), new DetailingDimensionLabel(225, 50, 120),
            new DetailingDimensionLabel(800, 400, 160) }, new[] { (Center: 400.0, Width: 300.0) }, 7.5, 7.5);
        Assert.True(plan[1].Row > 0);
        Assert.Equal(100, plan[0].Center); Assert.Equal(800, plan[2].Center);
    }
}
