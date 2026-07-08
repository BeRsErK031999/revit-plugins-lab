namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyTypeParameterInfo
{
    public FamilyTypeParameterInfo()
    {
    }

    public FamilyTypeParameterInfo(
        string name,
        string value,
        string storageType,
        string scope,
        string formula)
    {
        Name = name;
        Value = value;
        StorageType = storageType;
        Scope = scope;
        Formula = formula;
    }

    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string StorageType { get; set; } = string.Empty;

    public string Scope { get; set; } = string.Empty;

    public string Formula { get; set; } = string.Empty;

    public string ValueDisplay => string.IsNullOrWhiteSpace(Value) ? "-" : Value;

    public string FormulaDisplay => string.IsNullOrWhiteSpace(Formula) ? "-" : Formula;
}
