namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record VoltageDropSupplementaryInputs(
    double ThreePhasePower,
    double ThreePhaseCurrent,
    double NewThreePhasePower,
    double SinglePhasePower,
    double SinglePhaseCurrent,
    double LegacyResistivity,
    double LegacyLength,
    double LegacySection,
    double LegacyCosPhi,
    double LegacyReactance,
    double LegacyCurrent,
    double LineCurrent,
    double LineLength,
    double LineResistance,
    double LineReactance,
    double LineCosPhi,
    double LineSinPhi)
{
    public static VoltageDropSupplementaryInputs Default { get; } = new(
        ThreePhasePower: 40,
        ThreePhaseCurrent: 71.6,
        NewThreePhasePower: 10.2,
        SinglePhasePower: 33.4,
        SinglePhaseCurrent: 77,
        LegacyResistivity: 0.0225,
        LegacyLength: 63,
        LegacySection: 1.5,
        LegacyCosPhi: 0.95,
        LegacyReactance: 0.08,
        LegacyCurrent: 1.9,
        LineCurrent: 1.9,
        LineLength: 0.063,
        LineResistance: 13.35,
        LineReactance: 0.11,
        LineCosPhi: 0.95,
        LineSinPhi: 0.31);
}
