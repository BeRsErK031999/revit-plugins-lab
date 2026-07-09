namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetInfo(
    long ElementId,
    string CurrentNumber,
    string Name,
    bool IsPlaceholder,
    string GroupName = "Без группы");
