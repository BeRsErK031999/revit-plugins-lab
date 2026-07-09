using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.UI;
using WpfColor = System.Windows.Media.Color;

namespace TrueBIM.App.Modules.ViewVisibility.UI;

internal static class ViewVisibilityRibbonState
{
    private static readonly WpfColor VisibleEyeColor = WpfColor.FromRgb(0, 122, 204);
    private static readonly WpfColor HiddenEyeColor = WpfColor.FromRgb(142, 151, 163);
    private static readonly Dictionary<string, TrackedButton> TrackedButtons = new(StringComparer.Ordinal);
    private static PulldownButton? trackedPulldown;
    private static string pulldownTooltip = string.Empty;

    public static void RegisterPulldown(PulldownButton pulldownButton)
    {
        trackedPulldown = pulldownButton;
        pulldownTooltip = pulldownButton.ToolTip;
        SetPulldownState(null);
    }

    public static void RegisterItem(TrueBimRibbonPulldownItemDefinition definition, PushButton button)
    {
        if (GetTarget(definition.Name) is null)
        {
            return;
        }

        TrackedButtons[definition.Name] = new TrackedButton(definition.Name, definition.Tooltip, button);
        SetButtonState(button, definition.Tooltip, null);
    }

    public static void Update(Document? document, View? activeView)
    {
        if (document is null || activeView is null || activeView.IsTemplate)
        {
            foreach (TrackedButton trackedButton in TrackedButtons.Values)
            {
                SetButtonState(trackedButton.Button, trackedButton.BaseTooltip, null);
            }

            SetPulldownState(null);
            return;
        }

        bool? allCategoriesVisible = ResolveAllCategoriesVisibility(document, activeView, null);
        SetPulldownState(allCategoriesVisible);

        foreach (TrackedButton trackedButton in TrackedButtons.Values)
        {
            VisibilityTarget? target = GetTarget(trackedButton.Name);
            bool? isVisible = target is null
                ? null
                : ResolveTargetVisibility(document, activeView, target);
            SetButtonState(trackedButton.Button, trackedButton.BaseTooltip, isVisible);
        }
    }

    private static bool? ResolveTargetVisibility(Document document, View activeView, VisibilityTarget target)
    {
        if (target.AllCategories)
        {
            return ResolveAllCategoriesVisibility(document, activeView, null);
        }

        if (target.CategoryType is CategoryType categoryType)
        {
            return ResolveAllCategoriesVisibility(document, activeView, categoryType);
        }

        if (target.BuiltInCategory is BuiltInCategory builtInCategory)
        {
            Category? category = Category.GetCategory(document, builtInCategory);
            return category is null ? null : ResolveCategoryVisibility(activeView, category);
        }

        return null;
    }

    private static bool? ResolveAllCategoriesVisibility(Document document, View activeView, CategoryType? categoryType)
    {
        bool foundControllableCategory = false;
        bool allCategoriesVisible = true;

        foreach (Category category in document.Settings.Categories.Cast<Category>())
        {
            if (categoryType is not null && category.CategoryType != categoryType)
            {
                continue;
            }

            bool? isVisible = ResolveCategoryVisibility(activeView, category);
            if (isVisible is null)
            {
                continue;
            }

            foundControllableCategory = true;
            allCategoriesVisible &= isVisible.Value;
        }

        return foundControllableCategory ? allCategoriesVisible : null;
    }

    private static bool? ResolveCategoryVisibility(View activeView, Category category)
    {
        if (category.Id == ElementId.InvalidElementId || string.IsNullOrWhiteSpace(category.Name))
        {
            return null;
        }

        if (category.CategoryType is CategoryType.Invalid or CategoryType.Internal)
        {
            return null;
        }

        try
        {
            if (!activeView.CanCategoryBeHidden(category.Id))
            {
                return null;
            }

            return !activeView.GetCategoryHidden(category.Id);
        }
        catch (Exception exception) when (IsExpectedRevitCategoryException(exception))
        {
            return null;
        }
    }

    private static void SetPulldownState(bool? isVisible)
    {
        if (trackedPulldown is null)
        {
            return;
        }

        WpfColor color = isVisible == true ? VisibleEyeColor : HiddenEyeColor;
        trackedPulldown.Image = IconFactory.CreateImage(TrueBimIcon.Visibility, color);
        trackedPulldown.LargeImage = IconFactory.CreateImage(TrueBimIcon.Visibility, color);
        trackedPulldown.ToolTip = AppendStateTooltip(
            pulldownTooltip,
            isVisible,
            "Все доступные категории видимы.",
            "Есть скрытые доступные категории.",
            "Откройте обычный вид, чтобы увидеть состояние категорий.");
    }

    private static void SetButtonState(PushButton button, string baseTooltip, bool? isVisible)
    {
        WpfColor color = isVisible == true ? VisibleEyeColor : HiddenEyeColor;
        button.Image = IconFactory.CreateImage(TrueBimIcon.Visibility, color);
        button.LargeImage = IconFactory.CreateImage(TrueBimIcon.Visibility, color);
        button.ToolTip = AppendStateTooltip(
            baseTooltip,
            isVisible,
            "Сейчас видно на активном виде.",
            "Сейчас скрыто на активном виде.",
            "Недоступно для текущего активного вида.");
    }

    private static string AppendStateTooltip(
        string baseTooltip,
        bool? isVisible,
        string visibleText,
        string hiddenText,
        string unavailableText)
    {
        string stateText = isVisible switch
        {
            true => visibleText,
            false => hiddenText,
            _ => unavailableText
        };

        return string.IsNullOrWhiteSpace(baseTooltip)
            ? stateText
            : $"{baseTooltip} {stateText}";
    }

    private static VisibilityTarget? GetTarget(string buttonName)
    {
        return buttonName switch
        {
            "TrueBIM_ViewVisibility_All" => VisibilityTarget.All,
            "TrueBIM_ViewVisibility_Windows" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Windows),
            "TrueBIM_ViewVisibility_Doors" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Doors),
            "TrueBIM_ViewVisibility_Walls" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Walls),
            "TrueBIM_ViewVisibility_Columns" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Columns),
            "TrueBIM_ViewVisibility_StructuralFraming" => VisibilityTarget.ForCategory(BuiltInCategory.OST_StructuralFraming),
            "TrueBIM_ViewVisibility_StructuralFoundation" => VisibilityTarget.ForCategory(BuiltInCategory.OST_StructuralFoundation),
            "TrueBIM_ViewVisibility_Rebar" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Rebar),
            "TrueBIM_ViewVisibility_Components" => VisibilityTarget.ForCategory(BuiltInCategory.OST_SpecialityEquipment),
            "TrueBIM_ViewVisibility_GenericModels" => VisibilityTarget.ForCategory(BuiltInCategory.OST_GenericModel),
            "TrueBIM_ViewVisibility_Roofs" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Roofs),
            "TrueBIM_ViewVisibility_Floors" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Floors),
            "TrueBIM_ViewVisibility_Ceilings" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Ceilings),
            "TrueBIM_ViewVisibility_Stairs" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Stairs),
            "TrueBIM_ViewVisibility_Railings" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Railings),
            "TrueBIM_ViewVisibility_Ramps" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Ramps),
            "TrueBIM_ViewVisibility_Grids" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Grids),
            "TrueBIM_ViewVisibility_Levels" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Levels),
            "TrueBIM_ViewVisibility_Sections" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Sections),
            "TrueBIM_ViewVisibility_Elevations" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Elev),
            "TrueBIM_ViewVisibility_Tags" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Tags),
            "TrueBIM_ViewVisibility_ReferencePlanes" => VisibilityTarget.ForCategory(BuiltInCategory.OST_CLines),
            "TrueBIM_ViewVisibility_Lines" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Lines),
            "TrueBIM_ViewVisibility_Mass" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Mass),
            "TrueBIM_ViewVisibility_Ducts" => VisibilityTarget.ForCategory(BuiltInCategory.OST_DuctCurves),
            "TrueBIM_ViewVisibility_FlexDucts" => VisibilityTarget.ForCategory(BuiltInCategory.OST_FlexDuctCurves),
            "TrueBIM_ViewVisibility_Pipes" => VisibilityTarget.ForCategory(BuiltInCategory.OST_PipeCurves),
            "TrueBIM_ViewVisibility_Wires" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Wire),
            "TrueBIM_ViewVisibility_CableTrays" => VisibilityTarget.ForCategory(BuiltInCategory.OST_CableTray),
            "TrueBIM_ViewVisibility_Conduits" => VisibilityTarget.ForCategory(BuiltInCategory.OST_Conduit),
            "TrueBIM_ViewVisibility_MechanicalEquipment" => VisibilityTarget.ForCategory(BuiltInCategory.OST_MechanicalEquipment),
            "TrueBIM_ViewVisibility_ElectricalEquipment" => VisibilityTarget.ForCategory(BuiltInCategory.OST_ElectricalEquipment),
            "TrueBIM_ViewVisibility_AnalyticalModel" => VisibilityTarget.ForType(CategoryType.AnalyticalModel),
            "TrueBIM_ViewVisibility_PointClouds" => VisibilityTarget.ForCategory(BuiltInCategory.OST_PointClouds),
            "TrueBIM_ViewVisibility_Links" => VisibilityTarget.ForCategory(BuiltInCategory.OST_RvtLinks),
            "TrueBIM_ViewVisibility_ImportSymbols" => VisibilityTarget.ForCategory(BuiltInCategory.OST_ImportObjectStyles),
            "TrueBIM_ViewVisibility_RasterImages" => VisibilityTarget.ForCategory(BuiltInCategory.OST_RasterImages),
            "TrueBIM_ViewVisibility_GenericAnnotations" => VisibilityTarget.ForCategory(BuiltInCategory.OST_GenericAnnotation),
            _ => null
        };
    }

    private static bool IsExpectedRevitCategoryException(Exception exception)
    {
        return exception is Autodesk.Revit.Exceptions.ApplicationException
            or Autodesk.Revit.Exceptions.ArgumentException
            or ArgumentException
            or InvalidOperationException;
    }

    private sealed record TrackedButton(string Name, string BaseTooltip, PushButton Button);

    private sealed record VisibilityTarget(
        BuiltInCategory? BuiltInCategory,
        CategoryType? CategoryType,
        bool AllCategories)
    {
        public static VisibilityTarget All { get; } = new(null, null, true);

        public static VisibilityTarget ForCategory(BuiltInCategory builtInCategory)
        {
            return new VisibilityTarget(builtInCategory, null, false);
        }

        public static VisibilityTarget ForType(CategoryType categoryType)
        {
            return new VisibilityTarget(null, categoryType, false);
        }
    }
}
