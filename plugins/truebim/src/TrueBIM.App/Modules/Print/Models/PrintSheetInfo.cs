namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSheetInfo(
    long ElementId,
    string SourceName,
    string SheetNumber,
    string SheetName,
    string SheetFormat,
    bool IsPlaceholder,
    bool CanBePrinted);
