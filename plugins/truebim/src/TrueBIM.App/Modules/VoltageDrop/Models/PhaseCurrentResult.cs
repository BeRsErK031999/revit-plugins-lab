namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record PhaseCurrentResult(
    string Label,
    double CosPhi,
    double Current,
    string Note);
