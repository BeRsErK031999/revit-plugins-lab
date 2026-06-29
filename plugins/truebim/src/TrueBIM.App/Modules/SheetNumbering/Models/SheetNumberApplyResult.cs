namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberApplyResult(
    int ChangedCount,
    int UnchangedCount,
    int SkippedCount,
    int FailedCount)
{
    public int TotalCount => ChangedCount + UnchangedCount + SkippedCount + FailedCount;
}
