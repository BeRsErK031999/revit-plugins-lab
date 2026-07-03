namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record ApartmentDemandResult(
    double ApartmentSpecificDemand,
    double ApartmentInstalledPower,
    ElectricalLoadResult ApartmentLoad,
    double LiftDemandFactor,
    ElectricalLoadResult LiftLoad);
