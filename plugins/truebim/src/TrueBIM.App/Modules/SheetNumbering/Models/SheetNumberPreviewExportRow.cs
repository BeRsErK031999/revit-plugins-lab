namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberPreviewExportRow(
    long ElementId,
    string CurrentNumber,
    string NewNumber,
    string SheetName,
    bool IsPlaceholder,
    string Status);
