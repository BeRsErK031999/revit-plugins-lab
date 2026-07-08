namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public sealed record TitleBlockApplyResult(
    IReadOnlyList<TitleBlockPreviewRow> Rows)
{
    public int AppliedCount => Rows.Count(row => row.Status == "Готово");

    public int SkippedCount => Rows.Count(row => row.Status == "Пропущено");

    public int FailedCount => Rows.Count(row => row.Status == "Ошибка");
}
