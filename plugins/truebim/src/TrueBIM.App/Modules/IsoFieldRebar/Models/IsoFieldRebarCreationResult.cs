namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarCreationResult(
    int AddedCount,
    int UpdatedCount,
    int DeletedCount,
    int UnchangedCount,
    IReadOnlyList<long> CreatedElementIds,
    IReadOnlyList<long> DeletedElementIds,
    string Message)
{
    public int CreatedCount => AddedCount + UpdatedCount;
}
