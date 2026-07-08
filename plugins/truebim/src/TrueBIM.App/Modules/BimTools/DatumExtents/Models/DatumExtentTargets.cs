using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Models;

public static class DatumExtentTargets
{
    public const string Model = "Model";

    public const string ViewSpecific = "ViewSpecific";

    public static IReadOnlyList<DatumExtentTargetOption> Options { get; } =
    [
        new(ViewSpecific, "2D на активном виде"),
        new(Model, "3D модельные экстенты")
    ];

    public static DatumExtentType ToRevitType(string? value)
    {
        return string.Equals(value, Model, StringComparison.OrdinalIgnoreCase)
            ? DatumExtentType.Model
            : DatumExtentType.ViewSpecific;
    }

    public static string NormalizeProfileValue(string? value)
    {
        return string.Equals(value, Model, StringComparison.OrdinalIgnoreCase)
            ? Model
            : ViewSpecific;
    }

    public static string ToProfileValue(DatumExtentType value)
    {
        return value == DatumExtentType.Model ? Model : ViewSpecific;
    }

    public static string GetDisplayName(string? value)
    {
        string normalizedValue = NormalizeProfileValue(value);
        return Options.FirstOrDefault(option => string.Equals(option.Value, normalizedValue, StringComparison.OrdinalIgnoreCase))?.DisplayName
            ?? Options[0].DisplayName;
    }

    internal static string GetDisplayName(DatumExtentType value)
    {
        return GetDisplayName(ToProfileValue(value));
    }
}
