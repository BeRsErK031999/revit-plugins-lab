namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberChange(
    long ElementId,
    string CurrentNumber,
    string NewNumber)
{
    public bool IsChanged => CurrentNumber != NewNumber;
}
