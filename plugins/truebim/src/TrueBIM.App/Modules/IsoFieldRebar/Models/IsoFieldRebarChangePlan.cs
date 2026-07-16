namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldRebarChangeKind
{
    Add,
    Update,
    Delete,
    Unchanged
}

public sealed record IsoFieldRebarPlanItem(
    string StableId,
    string Signature);

public sealed record IsoFieldOwnedRebarSnapshot(
    long ElementId,
    string StableId,
    string? Signature);

public sealed record IsoFieldRebarChange(
    IsoFieldRebarChangeKind Kind,
    string StableId,
    IsoFieldRebarPlanItem? PlannedItem,
    IReadOnlyList<long> ExistingElementIds);

public sealed record IsoFieldRebarChangePlan(
    IReadOnlyList<IsoFieldRebarChange> Changes,
    IReadOnlyList<string> Diagnostics)
{
    public bool CanApply => Diagnostics.Count == 0;

    public int AddCount => Changes.Count(change => change.Kind == IsoFieldRebarChangeKind.Add);

    public int UpdateCount => Changes.Count(change => change.Kind == IsoFieldRebarChangeKind.Update);

    public int DeleteCount => Changes
        .Where(change => change.Kind == IsoFieldRebarChangeKind.Delete)
        .Sum(change => change.ExistingElementIds.Count);

    public int UnchangedCount => Changes.Count(change => change.Kind == IsoFieldRebarChangeKind.Unchanged);

    public int ExistingElementDeleteCount => Changes
        .Where(change => change.Kind is IsoFieldRebarChangeKind.Update or IsoFieldRebarChangeKind.Delete)
        .Sum(change => change.ExistingElementIds.Count);

    public bool HasChanges => AddCount > 0 || UpdateCount > 0 || DeleteCount > 0;

    public string Summary =>
        $"Добавить: {AddCount}; обновить: {UpdateCount}; удалить: {DeleteCount}; без изменений: {UnchangedCount}.";
}
