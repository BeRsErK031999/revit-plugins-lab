namespace TrueBIM.App.Modules.VoltageDrop.Models;

public sealed record VoltageDropInputs(
    double AluminumCoefficient400,
    double CopperCoefficient400,
    double CopperCoefficient230,
    double AluminumCoefficient230,
    double LineLength,
    double CableSection,
    double Power)
{
    public static VoltageDropInputs Default { get; } = new(
        AluminumCoefficient400: 44,
        CopperCoefficient400: 72.2,
        CopperCoefficient230: 12.1,
        AluminumCoefficient230: 7.7,
        LineLength: 310,
        CableSection: 150,
        Power: 40);
}
