namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed record ColorSwatch(byte Red, byte Green, byte Blue)
{
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";
}
