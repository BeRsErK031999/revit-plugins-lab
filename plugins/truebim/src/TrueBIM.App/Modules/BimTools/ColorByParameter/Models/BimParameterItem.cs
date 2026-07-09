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
        : this(parameterId, name, storageType, sourceKind, elementCount, categoryCount, Array.Empty<long>())
    {
    }

    public BimParameterItem(
        ElementId parameterId,
        string name,
        StorageType storageType,
        ParameterSourceKind sourceKind,
        int elementCount,
        int categoryCount,
        IEnumerable<long>? applicableCategoryIds)
    {
        ParameterId = parameterId;
        Name = string.IsNullOrWhiteSpace(name) ? "<без имени>" : name;
        StorageType = storageType;
        SourceKind = sourceKind;
        ElementCount = elementCount;
        CategoryCount = categoryCount;
        ApplicableCategoryIds = (applicableCategoryIds ?? Array.Empty<long>())
            .Distinct()
            .OrderBy(categoryId => categoryId)
            .ToArray();
    }

    public ElementId ParameterId { get; }

    public string Name { get; }

    public StorageType StorageType { get; }

    public ParameterSourceKind SourceKind { get; }

    public int ElementCount { get; }

    public int CategoryCount { get; }

    public IReadOnlyCollection<long> ApplicableCategoryIds { get; }

    public string SourceDisplay => SourceKind == ParameterSourceKind.Type ? "Тип" : "Экземпляр";

    public string StorageTypeDisplay => ParameterStorageTypeFormatter.Format(StorageType);

    public string DisplayName => $"{Name} | проектный | {SourceDisplay} | {StorageTypeDisplay} | элементов: {ElementCount}";

    public override string ToString()
    {
        return DisplayName;
    }
}
