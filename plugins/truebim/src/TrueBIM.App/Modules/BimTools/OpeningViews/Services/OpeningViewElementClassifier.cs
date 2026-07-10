using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewElementClassifier
{
    public static bool IsSupported(Element? source)
    {
        return IsDoor(source) || IsWindow(source) || IsCurtainWall(source);
    }

    public static bool IsDoor(Element? source)
    {
        return source is FamilyInstance && IsCategory(source, BuiltInCategory.OST_Doors);
    }

    public static bool IsWindow(Element? source)
    {
        return source is FamilyInstance && IsCategory(source, BuiltInCategory.OST_Windows);
    }

    public static bool IsCurtainWall(Element? source)
    {
        return source is Wall wall && wall.WallType.Kind == WallKind.Curtain;
    }

    public static string GetCategoryKey(Element source)
    {
        Guard.NotNull(source, nameof(source));
        if (IsCurtainWall(source))
        {
            return OpeningViewCategoryKeys.CurtainWall;
        }

        return IsWindow(source)
            ? OpeningViewCategoryKeys.Window
            : OpeningViewCategoryKeys.Door;
    }

    private static bool IsCategory(Element source, BuiltInCategory category)
    {
        return source.Category is not null
            && RevitElementIds.GetValue(source.Category.Id) == (long)category;
    }
}
