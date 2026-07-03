namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record ElectricalLoadResult(
    double ActivePower,
    double CosPhi,
    double ReactivePower,
    double ApparentPower,
    double Current);
