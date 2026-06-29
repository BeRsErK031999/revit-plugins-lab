namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberingPreviewResult(
    IReadOnlyList<SheetNumberPreview> Previews,
    IReadOnlyList<DuplicateSheetNumberIssue> DuplicateIssues)
{
    public bool HasBlockingIssues => DuplicateIssues.Count > 0;
}
