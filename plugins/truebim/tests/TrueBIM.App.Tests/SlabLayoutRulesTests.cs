using TrueBIM.App.Modules.SlabReinforcement;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class SlabLayoutRulesTests
{
    [Theory]
    [InlineData(1140, 200, 190, 50)]
    [InlineData(1250, 200, 200, 150)]
    [InlineData(1318, 203, 203, 203)]
    [InlineData(150, 200, 0, 50)]
    public void StationsPreserveMarginsAndCentralPitch(double width, double step, double preceding, double last)
    {
        var positions = SlabLayoutRules.Stations(width, step);
        var gaps = positions.Skip(1).Zip(positions, (b, a) => b - a).ToArray();
        Assert.Equal(50, positions[0]);
        Assert.Equal(width - 50, positions[^1], 6);
        Assert.Equal(last, gaps[^1]);
        if (gaps.Length > 1)
            Assert.Equal(preceding, gaps[^2]);
        Assert.All(gaps.SkipLast(2), gap => Assert.Equal(step, gap));
    }

    [Theory]
    [InlineData(104, 200)]
    [InlineData(1141, 200)]
    [InlineData(175, 55)]
    public void ImpossibleGridIsReported(double width, double step) =>
        Assert.Throws<InvalidOperationException>(() => SlabLayoutRules.Stations(width, step));

    [Theory]
    [InlineData(680, 30, true, 620)]
    [InlineData(709, 30, true, 645)]
    [InlineData(710, 30, false, 650)]
    [InlineData(184.99, 30, true, 120)]
    [InlineData(180, 30, true, 120)]
    [InlineData(179.99, 30, true, 0)]
    public void ShortSectionsUseClearLengthAndRoundDown(double concrete, double cover, bool shortSpan, double length)
    {
        double clear = concrete - 2 * cover;
        Assert.Equal(shortSpan, SlabLayoutRules.IsShortSpan(clear, 650));
        Assert.Equal(length, SlabLayoutRules.StirrupLength(clear));
    }

    [Theory]
    [InlineData(10, 650)]
    [InlineData(12, 750)]
    [InlineData(16, 1000)]
    public void CalculatesOneOutsideLegForB40AndA500C(double diameter, double expected) =>
        Assert.Equal(expected, SlabLayoutRules.LegLength(diameter, 435, 1.4));
}
