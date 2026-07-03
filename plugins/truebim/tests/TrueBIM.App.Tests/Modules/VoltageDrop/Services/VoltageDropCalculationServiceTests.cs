using TrueBIM.App.Modules.VoltageDrop.Models;
using TrueBIM.App.Modules.VoltageDrop.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.VoltageDrop.Services;

public sealed class VoltageDropCalculationServiceTests
{
    private readonly VoltageDropCalculationService service = new();

    [Fact]
    public void CalculateVoltageDrop_DefaultInputs_MatchesExcelFirstTable()
    {
        VoltageDropResult result = service.CalculateVoltageDrop(VoltageDropInputs.Default);

        AssertClose(12400, result.LoadMoment);
        AssertClose(1.8787878787878789, result.Aluminum400DropPercent);
        AssertClose(1.1449676823638042, result.Copper400DropPercent);
        AssertClose(6.8319559228650135, result.Copper230DropPercent);
        AssertClose(10.735930735930737, result.Aluminum230DropPercent);
        AssertClose(62.08750613114124, result.ThreePhaseCurrents[0].Current);
        AssertClose(93.608855397720617, result.ThreePhaseCurrents[6].Current);
        AssertClose(185.5287569573284, result.SinglePhaseCurrents[0].Current);
        AssertClose(279.72027972027968, result.SinglePhaseCurrents[6].Current);
    }

    [Fact]
    public void CalculateApartmentDemand_DefaultInputs_MatchesExcelRows67To68()
    {
        ApartmentDemandResult result = service.CalculateApartmentDemand(ApartmentDemandInputs.Default);

        AssertClose(2.1843750000000002, result.ApartmentSpecificDemand);
        AssertClose(250, result.ApartmentInstalledPower);
        AssertClose(49.148437500000007, result.ApartmentLoad.ActivePower);
        AssertClose(9.9800158910040402, result.ApartmentLoad.ReactivePower);
        AssertClose(50.151466836734699, result.ApartmentLoad.ApparentPower);
        AssertClose(76.197270732745707, result.ApartmentLoad.Current);
        AssertClose(0.8, result.LiftDemandFactor);
        AssertClose(24, result.LiftLoad.ActivePower);
        AssertClose(18, result.LiftLoad.ReactivePower);
        AssertClose(30, result.LiftLoad.ApparentPower);
        AssertClose(45.580284409707296, result.LiftLoad.Current);
    }

    [Fact]
    public void CalculateHighComfortApartmentDemand_DefaultInputs_MatchesExcelRows73To77()
    {
        ApartmentDemandResult standardResult = service.CalculateApartmentDemand(ApartmentDemandInputs.Default);
        HighComfortApartmentDemandResult result = service.CalculateHighComfortApartmentDemand(
            HighComfortApartmentDemandInputs.Default,
            standardResult.ApartmentLoad.ActivePower);

        AssertClose(0.8, result.UnitPowerDemandFactor);
        AssertClose(0.24, result.ApartmentCountDemandFactor);
        AssertClose(80.5, result.ApartmentInstalledPower);
        AssertClose(15.456000000000001, result.ApartmentLoad.ActivePower);
        AssertClose(3.1384746587591628, result.ApartmentLoad.ReactivePower);
        AssertClose(15.771428571428572, result.ApartmentLoad.ApparentPower);
        AssertClose(23.962206661103266, result.ApartmentLoad.Current);
        AssertClose(0.8, result.LiftDemandFactor);
        AssertClose(24, result.LiftLoad.ActivePower);
        AssertClose(18, result.LiftLoad.ReactivePower);
        AssertClose(30, result.LiftLoad.ApparentPower);
        AssertClose(45.580284409707296, result.LiftLoad.Current);
        AssertClose(64.604437500000003, result.CombinedApartmentLoad.ActivePower);
        AssertClose(13.118490549763241, result.CombinedApartmentLoad.ReactivePower);
        AssertClose(65.922895408163271, result.CombinedApartmentLoad.ApparentPower);
        AssertClose(100.15947739384897, result.CombinedApartmentLoad.Current);
    }

    [Fact]
    public void CalculateVoltageDrop_ZeroCableSection_ThrowsValidationError()
    {
        VoltageDropInputs inputs = VoltageDropInputs.Default with { CableSection = 0 };

        VoltageDropValidationException exception = Assert.Throws<VoltageDropValidationException>(
            () => service.CalculateVoltageDrop(inputs));

        VoltageDropValidationError error = Assert.Single(exception.Errors);
        Assert.Equal(nameof(VoltageDropInputs.CableSection), error.FieldKey);
        Assert.Contains("Сечение кабеля", error.Message, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void CalculateVoltageDrop_NaNPower_ThrowsValidationError()
    {
        VoltageDropInputs inputs = VoltageDropInputs.Default with { Power = double.NaN };

        VoltageDropValidationException exception = Assert.Throws<VoltageDropValidationException>(
            () => service.CalculateVoltageDrop(inputs));

        VoltageDropValidationError error = Assert.Single(exception.Errors);
        Assert.Equal(nameof(VoltageDropInputs.Power), error.FieldKey);
        Assert.Contains("Мощность", error.Message, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void CalculateApartmentDemand_UnsupportedApartmentCount_ThrowsValidationError()
    {
        ApartmentDemandInputs inputs = ApartmentDemandInputs.Default with { ApartmentCount = 1001 };

        VoltageDropValidationException exception = Assert.Throws<VoltageDropValidationException>(
            () => service.CalculateApartmentDemand(inputs));

        VoltageDropValidationError error = Assert.Single(exception.Errors);
        Assert.Equal(nameof(ApartmentDemandInputs.ApartmentCount), error.FieldKey);
        Assert.Contains("1..1000", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateApartmentDemand_FractionalElevatorCount_ThrowsValidationError()
    {
        ApartmentDemandInputs inputs = ApartmentDemandInputs.Default with { ElevatorCount = 2.5 };

        VoltageDropValidationException exception = Assert.Throws<VoltageDropValidationException>(
            () => service.CalculateApartmentDemand(inputs));

        VoltageDropValidationError error = Assert.Single(exception.Errors);
        Assert.Equal(nameof(ApartmentDemandInputs.ElevatorCount), error.FieldKey);
        Assert.Contains("целым числом", error.Message, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void CalculateHighComfortApartmentDemand_UnsupportedUnitPower_ThrowsValidationError()
    {
        HighComfortApartmentDemandInputs inputs = HighComfortApartmentDemandInputs.Default with { ApartmentUnitPower = 71 };

        VoltageDropValidationException exception = Assert.Throws<VoltageDropValidationException>(
            () => service.CalculateHighComfortApartmentDemand(inputs, 0));

        VoltageDropValidationError error = Assert.Single(exception.Errors);
        Assert.Equal(nameof(HighComfortApartmentDemandInputs.ApartmentUnitPower), error.FieldKey);
        Assert.Contains("1..70", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateHighComfortApartmentDemand_InvalidCosPhi_ThrowsValidationError()
    {
        HighComfortApartmentDemandInputs inputs = HighComfortApartmentDemandInputs.Default with { CombinedApartmentCosPhi = 1.2 };

        VoltageDropValidationException exception = Assert.Throws<VoltageDropValidationException>(
            () => service.CalculateHighComfortApartmentDemand(inputs, 0));

        VoltageDropValidationError error = Assert.Single(exception.Errors);
        Assert.Equal(nameof(HighComfortApartmentDemandInputs.CombinedApartmentCosPhi), error.FieldKey);
        Assert.Contains("cos", error.Message, StringComparison.CurrentCultureIgnoreCase);
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.Equal(expected, actual, precision: 9);
    }
}
