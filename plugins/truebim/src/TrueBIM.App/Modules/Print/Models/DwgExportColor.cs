namespace TrueBIM.App.Modules.Print.Models;

public readonly record struct DwgExportColor(byte Red, byte Green, byte Blue)
{
    public static DwgExportColor White { get; } = new(byte.MaxValue, byte.MaxValue, byte.MaxValue);

    public string ToHex()
    {
        return $"#{Red:X2}{Green:X2}{Blue:X2}";
    }

    public static bool TryParse(string? value, out DwgExportColor color)
    {
        color = White;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value!.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
        {
            normalized = normalized.Substring(1);
        }

        if (normalized.Length != 6)
        {
            return false;
        }

        try
        {
            color = new DwgExportColor(
                Convert.ToByte(normalized.Substring(0, 2), 16),
                Convert.ToByte(normalized.Substring(2, 2), 16),
                Convert.ToByte(normalized.Substring(4, 2), 16));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
