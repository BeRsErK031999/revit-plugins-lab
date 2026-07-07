using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldCoordinateMapperTests
{
    private readonly IsoFieldCoordinateMapper mapper = new();

    [Fact]
    public void MapToRevitPlaneFeet_MapsImageAnchorToRevitAnchor()
    {
        IsoFieldCalibration calibration = new(
            new IsoFieldPoint(100, 200),
            5,
            -2,
            304.8,
            true);

        IsoFieldPoint mapped = mapper.MapToRevitPlaneFeet(new IsoFieldPoint(100, 200), calibration);

        Assert.Equal(5, mapped.X);
        Assert.Equal(-2, mapped.Y);
    }

    [Fact]
    public void MapToRevitPlaneFeet_ConvertsPixelsToFeetAndInvertsImageY()
    {
        IsoFieldCalibration calibration = new(
            new IsoFieldPoint(100, 200),
            0,
            0,
            304.8,
            true);

        IsoFieldPoint mapped = mapper.MapToRevitPlaneFeet(new IsoFieldPoint(103, 198), calibration);

        Assert.Equal(3, mapped.X);
        Assert.Equal(2, mapped.Y);
    }

    [Fact]
    public void Validate_RejectsNonPositiveScale()
    {
        IsoFieldCalibration calibration = IsoFieldCalibration.Default with
        {
            MillimetersPerPixel = 0
        };

        Assert.Throws<InvalidOperationException>(() => mapper.Validate(calibration));
    }
}
