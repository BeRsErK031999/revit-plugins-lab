namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record DuplicateSheetNumberIssue(
    string SheetNumber,
    DuplicateSheetNumberIssueKind Kind,
    IReadOnlyList<SheetInfo> Sheets);
