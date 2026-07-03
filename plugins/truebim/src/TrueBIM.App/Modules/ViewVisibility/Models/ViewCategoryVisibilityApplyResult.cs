namespace TrueBIM.App.Modules.ViewVisibility.Models;

public sealed record ViewCategoryVisibilityApplyResult(
    int ShownCount,
    int HiddenCount,
    int UnchangedCount,
    int SkippedCount)
{
    public int UpdatedCount => ShownCount + HiddenCount;
}
