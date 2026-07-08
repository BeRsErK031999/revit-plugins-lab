using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.AutoMepDimensions;

public sealed class MepDimensionLinePlacementsTests
{
    [Fact]
    public void ResolveAlongCoordinate_KeepsCenterAsCurrentBehavior()
    {
        double coordinate = MepDimensionLinePlacements.ResolveAlongCoordinate(
            commonStart: 10,
            commonEnd: 20,
            MepDimensionLinePlacements.Center,
            offsetMillimeters: 1200);

        Assert.Equal(15, coordinate);
    }

    [Fact]
    public void ResolveAlongCoordinate_OffsetsBeforeAndAfterCommonSegment()
    {
        double before = MepDimensionLinePlacements.ResolveAlongCoordinate(
            commonStart: 10,
            commonEnd: 20,
            MepDimensionLinePlacements.Before,
            offsetMillimeters: 304.8);
        double after = MepDimensionLinePlacements.ResolveAlongCoordinate(
            commonStart: 10,
            commonEnd: 20,
            MepDimensionLinePlacements.After,
            offsetMillimeters: 304.8);

        Assert.Equal(9, before);
        Assert.Equal(21, after);
    }

    [Fact]
    public void NormalizeOffsetMillimeters_ClampsInvalidAndLargeOffsets()
    {
        Assert.Equal(0, MepDimensionLinePlacements.NormalizeOffsetMillimeters(double.NaN));
        Assert.Equal(0, MepDimensionLinePlacements.NormalizeOffsetMillimeters(-10));
        Assert.Equal(5000, MepDimensionLinePlacements.NormalizeOffsetMillimeters(6000));
    }
}
