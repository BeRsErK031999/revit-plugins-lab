using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed class BimCategoryItem
{
    public BimCategoryItem(ElementId categoryId, string name, int elementCount)
    {
        CategoryId = categoryId;
        Name = string.IsNullOrWhiteSpace(name) ? "<без категории>" : name;
        ElementCount = elementCount;
        IsSelected = true;
    }

    public ElementId CategoryId { get; }

    public string Name { get; }

    public int ElementCount { get; }

    public bool IsSelected { get; set; }

    public string DisplayName => $"{Name} ({ElementCount})";
}
