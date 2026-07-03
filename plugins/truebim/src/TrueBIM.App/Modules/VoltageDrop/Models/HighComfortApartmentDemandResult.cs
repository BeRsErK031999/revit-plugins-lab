namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record HighComfortApartmentDemandResult(
    double UnitPowerDemandFactor,
    double ApartmentCountDemandFactor,
    double ApartmentInstalledPower,
    ElectricalLoadResult ApartmentLoad,
    double LiftDemandFactor,
    ElectricalLoadResult LiftLoad,
    ElectricalLoadResult CombinedApartmentLoad);
