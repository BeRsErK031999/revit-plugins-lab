using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.ViewVisibility.Models;

public sealed record ViewCategoryVisibilityUpdate(ElementId CategoryId, bool IsVisible);
