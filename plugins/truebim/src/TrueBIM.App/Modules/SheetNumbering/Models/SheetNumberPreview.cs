namespace TrueBIM.App.Modules.SheetNumbering.Models;

public sealed record SheetNumberPreview(
    SheetInfo Sheet,
    string PreviewNumber)
{
    public bool IsChanged => Sheet.CurrentNumber != PreviewNumber;
}
