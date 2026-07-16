namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldReinforcementMode
{
    AdditionalOverBase,
    FullCombination
}

public sealed record IsoFieldEngineeringSettings(
    IsoFieldReinforcementMode Mode,
    double ConcreteCoverMillimeters,
    double BoundaryOffsetMillimeters,
    double MinimumBarLengthMillimeters,
    int MaximumBarCount = 5000)
{
    public static IsoFieldEngineeringSettings Default { get; } = new(
        IsoFieldReinforcementMode.AdditionalOverBase,
        ConcreteCoverMillimeters: 30,
        BoundaryOffsetMillimeters: 30,
        MinimumBarLengthMillimeters: 300);
}
