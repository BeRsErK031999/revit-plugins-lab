using TrueBIM.App.Modules.IsoFieldRebar.Models;
using TrueBIM.App.Modules.IsoFieldRebar.Services;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class IsoFieldArrayFamilyCoordinatesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(Math.PI / 2)]
    [InlineData(Math.PI)]
    [InlineData(-Math.PI / 3)]
    public void GetInsertionPoint_CentresSuppliedFamilyOnPlannedRectangle(double angle)
    {
        const double startX = 10;
        const double startY = -5;
        const double elevation = 3;
        const double length = 8;
        const double width = 2;
        double axisX = Math.Cos(angle);
        double axisY = Math.Sin(angle);
        IsoFieldRebarPoint3D firstStart = new(startX, startY, elevation);
        IsoFieldRebarPoint3D firstEnd = new(startX + axisX * length, startY + axisY * length, elevation);
        IsoFieldRebarPoint3D lastStart = new(startX + axisY * width, startY - axisX * width, elevation);
        IsoFieldRebarComponent component = new(12, 304.8, 1, 2);
        RebarRule rule = new("rule", "Slab", component.BarTypeName, component.SpacingMillimeters);
        IsoFieldArrayRebarPlacement placement = new(
            "zone", "Zone", rule, component, firstStart, firstEnd, lastStart,
            new IsoFieldRebarPoint3D(0, 0, 1), 3, "zone-array", ["source"]);

        IsoFieldRebarPoint3D insertion = IsoFieldArrayFamilyCoordinates.GetInsertionPoint(placement);

        Assert.Equal(startX + axisX * length / 2 + axisY * width / 2, insertion.XFeet, 8);
        Assert.Equal(startY + axisY * length / 2 - axisX * width / 2, insertion.YFeet, 8);
        Assert.Equal(elevation, insertion.ZFeet, 8);
        // Moving from the centre by negative half-length and half-width returns the first bar start.
        Assert.Equal(firstStart.XFeet, insertion.XFeet - axisX * length / 2 - axisY * width / 2, 8);
        Assert.Equal(firstStart.YFeet, insertion.YFeet - axisY * length / 2 + axisX * width / 2, 8);
    }
}
