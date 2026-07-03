using TrueBIM.App.Modules.VoltageDrop.Models;
using TrueBIM.App.Modules.VoltageDrop.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.VoltageDrop.Services;

public sealed class VoltageDropReferenceCatalogTests
{
    private readonly VoltageDropReferenceCatalog catalog = VoltageDropReferenceCatalog.Default;

    [Fact]
    public void VoltageDropCoefficients_ContainFirstSheetReferenceValues()
    {
        Assert.Equal(4, catalog.VoltageDropCoefficients.Count);
        AssertClose(44, catalog.GetVoltageDropCoefficient(VoltageDropConductorMaterial.Aluminum, 400));
        AssertClose(72.2, catalog.GetVoltageDropCoefficient(VoltageDropConductorMaterial.Copper, 400));
        AssertClose(12.1, catalog.GetVoltageDropCoefficient(VoltageDropConductorMaterial.Copper, 230));
        AssertClose(7.7, catalog.GetVoltageDropCoefficient(VoltageDropConductorMaterial.Aluminum, 230));
    }

    [Fact]
    public void StandardApartmentSpecificDemand_InterpolatesExcelG67()
    {
        AssertClose(2.1843750000000002, catalog.CalculateStandardApartmentSpecificDemand(25));
    }

    [Fact]
    public void HighComfortApartmentCountDemandFactor_InterpolatesExcelG74()
    {
        AssertClose(0.24, catalog.CalculateHighComfortApartmentCountDemandFactor(24));
    }

    [Fact]
    public void HighComfortUnitPowerDemandFactor_MatchesExcelG73Default()
    {
        AssertClose(0.8, catalog.CalculateHighComfortUnitPowerDemandFactor(11.5));
    }

    [Theory]
    [InlineData(7, 2, 0.8)]
    [InlineData(12, 2, 0.9)]
    [InlineData(7, 12, 0.48)]
    [InlineData(12, 12, 0.58)]
    public void LiftDemandFactor_MatchesExcelPiecewiseRules(double floors, double elevatorCount, double expected)
    {
        AssertClose(expected, catalog.CalculateLiftDemandFactor(floors, elevatorCount));
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.Equal(expected, actual, precision: 9);
    }
}
