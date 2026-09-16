namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;

public enum FamilyReplacementAlignment
{
    InsertionPoint,
    GeometryCenter
}

public sealed record FamilyReplacementItemResult(long SourceId, long? NewId, bool Replaced, string Message);

public sealed record FamilyReplacementResult(IReadOnlyList<FamilyReplacementItemResult> Items)
{
    public int Total => Items.Count;

    public int Replaced => Items.Count(item => item.Replaced);

    public int Skipped => Total - Replaced;

    public string Summary => $"Исходных элементов: {Total}. Заменено: {Replaced}. Пропущено: {Skipped}.";
}
