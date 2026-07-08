namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public static class OpeningViewOrientationSources
{
    public const string ElementFacing = "ElementFacing";
    public const string HostWall = "HostWall";

    public static IReadOnlyList<OpeningViewOrientationSourceOption> Options { get; } =
    [
        new(ElementFacing, "По элементу"),
        new(HostWall, "По стене")
    ];

    public static string NormalizeKey(string? value)
    {
        return Options.Any(option => option.Key.Equals(value, StringComparison.OrdinalIgnoreCase))
            ? Options.First(option => option.Key.Equals(value, StringComparison.OrdinalIgnoreCase)).Key
            : ElementFacing;
    }

    public static string GetDisplayName(string? value)
    {
        string key = NormalizeKey(value);
        return Options.First(option => option.Key.Equals(key, StringComparison.Ordinal)).DisplayName;
    }
}
