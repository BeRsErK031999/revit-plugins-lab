using TrueBIM.App.Modules.Lintels.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class LintelElevationAnnotationTextTests
{
    private const double FeetPerMeter = 1.0 / 0.3048;

    [Theory]
    [InlineData(12.345, "+12.345")]
    [InlineData(-2.75, "-2.750")]
    [InlineData(0, "±0.000")]
    [InlineData(0.0004, "±0.000")]
    public void Create_FormatsSignedMetersForAnnotation(double meters, string expected)
    {
        LintelElevationAnnotationText text =
            LintelElevationAnnotationText.Create(meters * FeetPerMeter);

        Assert.Equal("отм.", text.UpperLine);
        Assert.Equal(expected, text.LowerLine);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Create_RejectsNonFiniteElevation(double elevationFeet)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LintelElevationAnnotationText.Create(elevationFeet));
    }
}
