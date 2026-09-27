using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldArrayFabricationLengthServiceTests
{
    private readonly IsoFieldArrayFabricationLengthService service = new();

    [Theory]
    [InlineData(1089, 1170)]
    [InlineData(1500, 1670)]
    [InlineData(2340, 2340)]
    [InlineData(2340.1, 2920)]
    [InlineData(10400, 10400)]
    [InlineData(11700, 11700)]
    public void NormalizeMinimumLengthMillimeters_SelectsNextVerifiedFamilyLengthWithoutReducingMinimum(double minimum, double expected)
    {
        double actual = service.NormalizeMinimumLengthMillimeters(minimum);

        Assert.Equal(expected, actual);
        Assert.True(actual >= minimum);
    }

    [Fact]
    public void NormalizeMinimumLengthMillimeters_RejectsLengthBeyondVerifiedFamilyLimit()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            service.NormalizeMinimumLengthMillimeters(11700.1));

        Assert.Contains("11700", exception.Message, StringComparison.Ordinal);
        Assert.Contains("разделение пятна", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1170)]
    [InlineData(1950)]
    [InlineData(3900)]
    [InlineData(7800)]
    public void NormalizeMinimumLengthMillimeters_DoesNotAdvanceStockSizeBecauseOfFeetConversionNoise(double expected)
    {
        double sourceLengthMillimeters = expected - 1000;
        double minimumAlong = -sourceLengthMillimeters / 2 / 304.8 - 500 / 304.8;
        double maximumAlong = sourceLengthMillimeters / 2 / 304.8 + 500 / 304.8;
        double requiredMillimeters = (maximumAlong - minimumAlong) * 304.8;

        Assert.Equal(expected, service.NormalizeMinimumLengthMillimeters(requiredMillimeters));
        Assert.Equal(1300, service.NormalizeMinimumLengthMillimeters(1170.0001));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NormalizeMinimumLengthMillimeters_RejectsInvalidMinimum(double minimum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => service.NormalizeMinimumLengthMillimeters(minimum));
    }
}
