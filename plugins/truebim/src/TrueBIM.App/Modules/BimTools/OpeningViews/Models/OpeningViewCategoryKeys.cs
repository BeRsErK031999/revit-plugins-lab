namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public static class OpeningViewCategoryKeys
{
    public const string Door = "Door";

    public const string Window = "Window";

    public const string CurtainWall = "CurtainWall";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, Window, StringComparison.OrdinalIgnoreCase))
        {
            return Window;
        }

        return string.Equals(value, CurtainWall, StringComparison.OrdinalIgnoreCase)
            ? CurtainWall
            : Door;
    }
}
