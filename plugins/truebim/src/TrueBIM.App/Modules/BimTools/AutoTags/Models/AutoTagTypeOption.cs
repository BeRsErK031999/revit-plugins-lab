namespace TrueBIM.App.Modules.BimTools.AutoTags.Models;

public sealed record AutoTagTypeOption(
    long ElementId,
    string FamilyName,
    string TypeName,
    string CategoryName)
{
    public static AutoTagTypeOption Automatic { get; } = new(-1, "Авто", "По категории", string.Empty);

    public bool IsAutomatic => ElementId < 0;

    public string DisplayName => IsAutomatic
        ? "Авто (по категории)"
        : $"{FamilyName}: {TypeName} [{CategoryName}]";
}
