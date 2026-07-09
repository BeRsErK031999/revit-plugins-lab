namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyMetadataResult
{
    public bool Succeeded { get; set; }

    public string Category { get; set; } = FamilyManagerDefaults.UnknownCategory;

    public List<FamilyTypeInfo> Types { get; set; } = [];

    public string TypeCatalogPath { get; set; } = string.Empty;

    public List<string> TypeCatalogTypeNames { get; set; } = [];

    public string Message { get; set; } = string.Empty;
}
