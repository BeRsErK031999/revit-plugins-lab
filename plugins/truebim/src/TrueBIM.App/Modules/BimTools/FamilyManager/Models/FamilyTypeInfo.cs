namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyTypeInfo
{
    public FamilyTypeInfo()
    {
    }

    public FamilyTypeInfo(long elementId, string name)
        : this(elementId, name, [])
    {
    }

    public FamilyTypeInfo(long elementId, string name, IEnumerable<FamilyTypeParameterInfo>? parameters)
    {
        ElementId = elementId;
        Name = name;
        Parameters = parameters?.ToList() ?? [];
    }

    public long ElementId { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<FamilyTypeParameterInfo> Parameters { get; set; } = [];

    public string ParameterCountDisplay => Parameters.Count == 0
        ? "-"
        : Parameters.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
