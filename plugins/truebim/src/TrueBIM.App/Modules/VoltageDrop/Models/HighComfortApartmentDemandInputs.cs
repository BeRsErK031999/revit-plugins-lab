namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record HighComfortApartmentDemandInputs(
    double Floors,
    double ApartmentCount,
    double SpecificDemandApartmentCount,
    double ElevatorCount,
    double ApartmentUnitPower,
    double ApartmentUsageFactor,
    double ApartmentCoincidenceFactor,
    double ApartmentCosPhi,
    double LiftInstalledPower,
    double LiftCoincidenceFactor,
    double LiftCosPhi,
    double CombinedApartmentCosPhi)
{
    public static HighComfortApartmentDemandInputs Default { get; } = new(
        Floors: 8,
        ApartmentCount: 7,
        SpecificDemandApartmentCount: 24,
        ElevatorCount: 2,
        ApartmentUnitPower: 11.5,
        ApartmentUsageFactor: 1,
        ApartmentCoincidenceFactor: 1,
        ApartmentCosPhi: 0.98,
        LiftInstalledPower: 30,
        LiftCoincidenceFactor: 1,
        LiftCosPhi: 0.8,
        CombinedApartmentCosPhi: 0.98);
}
