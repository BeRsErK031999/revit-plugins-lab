namespace TrueBIM.App.Modules.BimTools.ParaManager.Models;

public sealed class ProjectParameterRow
{
    public ProjectParameterRow(
        string name,
        string dataTypeDisplay,
        string bindingTypeDisplay,
        string categoriesDisplay,
        string groupDisplay,
        bool isShared,
        string guidDisplay)
    {
        Name = name;
        DataTypeDisplay = dataTypeDisplay;
        BindingTypeDisplay = bindingTypeDisplay;
        CategoriesDisplay = categoriesDisplay;
        GroupDisplay = groupDisplay;
        IsShared = isShared;
        GuidDisplay = guidDisplay;
    }

    public string Name { get; }

    public string DataTypeDisplay { get; }

    public string BindingTypeDisplay { get; }

    public string CategoriesDisplay { get; }

    public string GroupDisplay { get; }

    public bool IsShared { get; }

    public string GuidDisplay { get; }
}
