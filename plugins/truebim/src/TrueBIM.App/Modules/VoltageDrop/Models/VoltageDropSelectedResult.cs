namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record VoltageDropSelectedResult(
    VoltageDropCoefficientEntry Coefficient,
    double LoadMoment,
    double DropPercent);
