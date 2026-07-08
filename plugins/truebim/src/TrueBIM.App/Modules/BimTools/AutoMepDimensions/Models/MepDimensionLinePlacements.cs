using System.Globalization;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;

public static class MepDimensionLinePlacements
{
    private const double FeetPerMillimeter = 1.0 / 304.8;
    private const double MaxOffsetMillimeters = 5000.0;

    public const string Center = "Center";
    public const string Before = "Before";
    public const string After = "After";

    public static IReadOnlyList<MepDimensionLinePlacementOption> Options { get; } =
    [
        new(Center, "По центру"),
        new(Before, "До трасс"),
        new(After, "После трасс")
    ];

    public static string NormalizeKey(string? value)
    {
        return Options.Any(option => option.Key.Equals(value, StringComparison.OrdinalIgnoreCase))
            ? Options.First(option => option.Key.Equals(value, StringComparison.OrdinalIgnoreCase)).Key
            : Center;
    }

    public static double NormalizeOffsetMillimeters(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        return value > MaxOffsetMillimeters ? MaxOffsetMillimeters : value;
    }

    public static double ResolveAlongCoordinate(
        double commonStart,
        double commonEnd,
        string? placement,
        double offsetMillimeters)
    {
        double offsetFeet = NormalizeOffsetMillimeters(offsetMillimeters) * FeetPerMillimeter;
        return NormalizeKey(placement) switch
        {
            Before => commonStart - offsetFeet,
            After => commonEnd + offsetFeet,
            _ => (commonStart + commonEnd) * 0.5
        };
    }

    public static string GetDisplayName(string? placement)
    {
        string normalized = NormalizeKey(placement);
        return Options.First(option => option.Key.Equals(normalized, StringComparison.Ordinal)).DisplayName;
    }

    public static string FormatForDisplay(string? placement, double offsetMillimeters)
    {
        string normalized = NormalizeKey(placement);
        if (normalized == Center)
        {
            return GetDisplayName(normalized);
        }

        return $"{GetDisplayName(normalized)}, {FormatMillimeters(offsetMillimeters)} мм";
    }

    public static string FormatForMessage(string? placement, double offsetMillimeters)
    {
        return $"Линия: {FormatForDisplay(placement, offsetMillimeters)}.";
    }

    public static string FormatMillimeters(double value)
    {
        return NormalizeOffsetMillimeters(value).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
