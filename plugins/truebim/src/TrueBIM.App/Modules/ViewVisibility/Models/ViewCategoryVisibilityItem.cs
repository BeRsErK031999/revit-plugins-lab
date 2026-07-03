using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.ViewVisibility.Models;

public sealed record ViewCategoryVisibilityItem(
    ElementId CategoryId,
    string Name,
    CategoryType CategoryType,
    bool IsVisible);
