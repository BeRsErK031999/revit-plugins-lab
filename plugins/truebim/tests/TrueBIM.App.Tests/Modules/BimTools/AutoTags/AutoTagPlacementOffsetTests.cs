using TrueBIM.App.Modules.BimTools.AutoTags.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoTags;

public sealed class AutoTagPlacementOffsetTests
{
    [Fact]
    public void Apply_OffsetsPointAlongViewRightAndUpDirections()
    {
        (double x, double y, double z) = AutoTagPlacementOffset.ApplyCoordinates(
            10,
            20,
            30,
            1,
            0,
            0,
            0,
            1,
            0,
            304.8,
            -152.4);

        Assert.Equal(11, x, 6);
        Assert.Equal(19.5, y, 6);
        Assert.Equal(30, z, 6);
    }

    [Fact]
    public void FormatForReport_ReturnsEmptyTextForZeroOffset()
    {
        string text = AutoTagPlacementOffset.FormatForReport(0, 0);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void NormalizeMillimeters_ClampsLargeValuesAndDropsInvalidValues()
    {
        Assert.Equal(5000, AutoTagPlacementOffset.NormalizeMillimeters(6000));
        Assert.Equal(-5000, AutoTagPlacementOffset.NormalizeMillimeters(-6000));
        Assert.Equal(0, AutoTagPlacementOffset.NormalizeMillimeters(double.PositiveInfinity));
    }
}
