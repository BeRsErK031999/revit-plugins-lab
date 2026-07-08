namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed record ColorSwatch(byte Red, byte Green, byte Blue)
{
    public string Hex => $"#{Red:X2}{Green:X2}{Blue:X2}";

    public static bool TryParseHex(string? value, out ColorSwatch color)
    {
        color = new ColorSwatch(0, 0, 0);
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        if (normalized.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(normalized.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out byte red)
            || !byte.TryParse(normalized.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out byte green)
            || !byte.TryParse(normalized.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out byte blue))
        {
            return false;
        }

        color = new ColorSwatch(red, green, blue);
        return true;
    }
}
