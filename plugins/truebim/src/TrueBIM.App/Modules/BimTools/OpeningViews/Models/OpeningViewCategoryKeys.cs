namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public static class OpeningViewCategoryKeys
{
    public const string Door = "Door";

    public const string Window = "Window";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Window, StringComparison.OrdinalIgnoreCase)
            ? Window
            : Door;
    }
}
