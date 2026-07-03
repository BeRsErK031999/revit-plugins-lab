namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record VoltageDropResult(
    double LoadMoment,
    double Aluminum400DropPercent,
    double Copper400DropPercent,
    double Copper230DropPercent,
    double Aluminum230DropPercent,
    IReadOnlyList<PhaseCurrentResult> ThreePhaseCurrents,
    IReadOnlyList<PhaseCurrentResult> SinglePhaseCurrents);
