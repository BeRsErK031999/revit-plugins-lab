namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record VoltageDropSupplementaryResult(
    double ThreePhaseVoltageFactor,
    double SinglePhaseVoltageFactor,
    double ThreePhaseCosPhi,
    double SinglePhaseCosPhi,
    double NewThreePhaseCurrent,
    double LegacySinPhi,
    double LegacyDropPercent,
    double LegacyVoltageDrop,
    double LegacyVoltageDropPercent,
    double LineVoltageDrop,
    double LineVoltageDropPercent);
