namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSheetInfo(
    long ElementId,
    string SourceId,
    string SourceName,
    bool SourceIsLinked,
    string GroupName,
    string SheetNumber,
    string SheetName,
    string SheetFormat,
    bool IsPlaceholder,
    bool CanBePrinted,
    IReadOnlyDictionary<string, string> SheetParameters,
    IReadOnlyDictionary<string, string> TitleBlockParameters);
