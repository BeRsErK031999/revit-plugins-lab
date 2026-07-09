namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyTypeCatalogInfo
{
    public string Path { get; set; } = string.Empty;

    public List<string> TypeNames { get; set; } = [];

    public bool Exists => !string.IsNullOrWhiteSpace(Path);
}
