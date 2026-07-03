namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record VoltageDropCoefficientEntry(
    VoltageDropConductorMaterial Material,
    double Voltage,
    double Coefficient,
    string Description);
