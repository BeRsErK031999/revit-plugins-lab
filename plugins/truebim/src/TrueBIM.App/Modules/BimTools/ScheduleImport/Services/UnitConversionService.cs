namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class UnitConversionService
{
    public double ToFeet(double value, string? unit)
    {
        string normalized = (unit ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "mm" or "мм" => value / 304.8,
            "cm" or "см" => value / 30.48,
            "m" or "м" => value / 0.3048,
            "ft" or "фут" or "feet" => value,
            _ => value
        };
    }
}
