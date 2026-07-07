namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarCreationResult(
    int CreatedCount,
    IReadOnlyList<long> CreatedElementIds,
    string Message);
