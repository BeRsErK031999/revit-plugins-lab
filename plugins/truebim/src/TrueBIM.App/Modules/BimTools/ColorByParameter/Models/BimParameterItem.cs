using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Models;

public sealed class BimParameterItem
{
    public BimParameterItem(
        ElementId parameterId,
        string name,
        StorageType storageType,
        ParameterSourceKind sourceKind,
        int elementCount,
        int categoryCount)
    {
        ParameterId = parameterId;
        Name = string.IsNullOrWhiteSpace(name) ? "<без имени>" : name;
        StorageType = storageType;
        SourceKind = sourceKind;
        ElementCount = elementCount;
        CategoryCount = categoryCount;
    }

    public ElementId ParameterId { get; }

    public string Name { get; }

    public StorageType StorageType { get; }

    public ParameterSourceKind SourceKind { get; }

    public int ElementCount { get; }

    public int CategoryCount { get; }

    public string SourceDisplay => SourceKind == ParameterSourceKind.Type ? "Тип" : "Экземпляр";

    public string StorageTypeDisplay => ParameterStorageTypeFormatter.Format(StorageType);

    public string DisplayName => $"{Name} | {SourceDisplay} | {StorageTypeDisplay} | элементов: {ElementCount}";

    public override string ToString()
    {
        return DisplayName;
    }
}
