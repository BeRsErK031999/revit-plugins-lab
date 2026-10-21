namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed record OwnedViewFilterRow(long Id, string Name, string Categories, string Rule)
{
    public bool IsSelected { get; set; }
}
