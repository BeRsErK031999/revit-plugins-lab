using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Models;

public sealed class CopyParameterRow
{
    public CopyParameterRow(
        ParameterIdentity identity,
        ParameterValueSnapshot value,
        ParameterSourceKind sourceKind,
        string warning,
        bool isSelected = false)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Value = value ?? throw new ArgumentNullException(nameof(value));
        SourceKind = sourceKind;
        Warning = warning;
        IsSelected = isSelected;
    }

    public ParameterIdentity Identity { get; }

    public ParameterValueSnapshot Value { get; }

    public ParameterSourceKind SourceKind { get; }

    public string ParameterName => Identity.Name;

    public string ValueDisplay => Value.DisplayValue;

    public StorageType StorageType => Identity.StorageType;

    public string StorageTypeDisplay => ParameterStorageTypeFormatter.Format(StorageType);

    public string SourceDisplay => SourceKind == ParameterSourceKind.Instance ? "Экземпляр" : "Тип";

    public string Warning { get; }

    public bool HasWarning => !string.IsNullOrWhiteSpace(Warning);

    public bool IsSelected { get; set; }
}
