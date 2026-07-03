namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record ApartmentDemandInputs(
    double Floors,
    double ApartmentCount,
    double ElevatorCount,
    double ApartmentUnitPower,
    double ApartmentUsageFactor,
    double ApartmentCoincidenceFactor,
    double ApartmentCosPhi,
    double LiftInstalledPower,
    double LiftCoincidenceFactor,
    double LiftCosPhi)
{
    public static ApartmentDemandInputs Default { get; } = new(
        Floors: 7,
        ApartmentCount: 25,
        ElevatorCount: 2,
        ApartmentUnitPower: 10,
        ApartmentUsageFactor: 1,
        ApartmentCoincidenceFactor: 0.9,
        ApartmentCosPhi: 0.98,
        LiftInstalledPower: 30,
        LiftCoincidenceFactor: 1,
        LiftCosPhi: 0.8);
}
