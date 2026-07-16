namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarComponent(
    double DiameterMillimeters,
    double SpacingMillimeters,
    int CombinationIndex,
    int CombinationCount)
{
    public bool IsBase => CombinationIndex == 0;

    public double AreaSquareCentimetersPerMeter =>
        Math.PI * DiameterMillimeters * DiameterMillimeters / 4
        * (1000 / SpacingMillimeters)
        / 100;

    public string BarTypeName => $"Ø{DiameterMillimeters:0.###} A500";

    public string DisplayName => $"d{DiameterMillimeters:0.###}s{SpacingMillimeters:0.###}";
}

public sealed record IsoFieldReinforcementCombination(
    string SourceLabel,
    IReadOnlyList<IsoFieldRebarComponent> Components)
{
    public double AreaSquareCentimetersPerMeter =>
        Components.Sum(component => component.AreaSquareCentimetersPerMeter);
}
