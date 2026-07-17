using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class ParameterCatalogService
{
    private readonly ITrueBimLogger logger;

    public ParameterCatalogService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static FinishScheduleParameterCategories TargetCategories { get; } = new(
        new ParameterCategoryReference((long)BuiltInCategory.OST_Rooms, "Помещения"),
        new ParameterCategoryReference((long)BuiltInCategory.OST_Walls, "Стены"),
        new ParameterCategoryReference((long)BuiltInCategory.OST_Floors, "Перекрытия"));

    public ParameterCatalog Collect(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        Dictionary<string, ParameterBucket> buckets = new(StringComparer.Ordinal);
        CollectCategory(document, BuiltInCategory.OST_Rooms, TargetCategories.Rooms, buckets);
        CollectCategory(document, BuiltInCategory.OST_Walls, TargetCategories.Walls, buckets);
        CollectCategory(document, BuiltInCategory.OST_Floors, TargetCategories.Floors, buckets);

        ParameterCatalog catalog = new(buckets.Values.Select(bucket => bucket.ToCatalogItem()));
        logger.Info(
            $"Finish Schedule parameter catalog collected {catalog.Items.Count} variants from document '{document.Title}'.");
        return catalog;
    }

    private void CollectCategory(
        Document document,
        BuiltInCategory builtInCategory,
        ParameterCategoryReference category,
        Dictionary<string, ParameterBucket> buckets)
    {
        IList<Element> instances = new FilteredElementCollector(document)
            .OfCategory(builtInCategory)
            .WhereElementIsNotElementType()
            .ToElements();
        HashSet<long> collectedTypeIds = [];

        foreach (Element instance in instances)
        {
            CollectElement(instance, ParameterBindingKind.Instance, category, buckets);

            ElementId typeId = instance.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                continue;
            }

            long typeIdValue = RevitElementIds.GetValue(typeId);
            if (!collectedTypeIds.Add(typeIdValue))
            {
                continue;
            }

            Element? type = document.GetElement(typeId);
            if (type is not null)
            {
                CollectElement(type, ParameterBindingKind.Type, category, buckets);
            }
        }
    }

    private void CollectElement(
        Element element,
        ParameterBindingKind bindingKind,
        ParameterCategoryReference category,
        Dictionary<string, ParameterBucket> buckets)
    {
        try
        {
            foreach (Parameter parameter in element.Parameters)
            {
                ParameterReference? reference = CreateReference(parameter, bindingKind);
                if (reference is null)
                {
                    continue;
                }

                if (!buckets.TryGetValue(reference.StableKey, out ParameterBucket? bucket))
                {
                    bucket = new ParameterBucket(reference);
                    buckets.Add(reference.StableKey, bucket);
                }

                bucket.AddSample(category.Id, parameter.IsReadOnly);
            }
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"Finish Schedule skipped parameters of element {RevitElementIds.GetValue(element.Id)}: {exception.Message}");
        }
    }

    private static ParameterReference? CreateReference(
        Parameter parameter,
        ParameterBindingKind bindingKind)
    {
        Definition? definition = parameter.Definition;
        if (definition is null || parameter.Id == ElementId.InvalidElementId)
        {
            return null;
        }

        string name = definition.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        ParameterStorageKind storageKind = MapStorageKind(parameter.StorageType);
        long definitionElementId = RevitElementIds.GetValue(parameter.Id);

        if (definition is InternalDefinition internalDefinition
            && internalDefinition.BuiltInParameter != BuiltInParameter.INVALID)
        {
            return ParameterReference.BuiltIn(
                name,
                (long)internalDefinition.BuiltInParameter,
                bindingKind,
                storageKind,
                definitionElementId);
        }

        if (parameter.IsShared)
        {
            Guid guid = parameter.GUID;
            if (guid != Guid.Empty)
            {
                return ParameterReference.Shared(
                    name,
                    guid,
                    definitionElementId,
                    bindingKind,
                    storageKind);
            }
        }

        return ParameterReference.Project(
            name,
            definitionElementId,
            bindingKind,
            storageKind);
    }

    private static ParameterStorageKind MapStorageKind(StorageType storageType)
    {
        return storageType switch
        {
            StorageType.String => ParameterStorageKind.String,
            StorageType.Integer => ParameterStorageKind.Integer,
            StorageType.Double => ParameterStorageKind.Double,
            StorageType.ElementId => ParameterStorageKind.ElementId,
            StorageType.None => ParameterStorageKind.None,
            _ => ParameterStorageKind.None
        };
    }

    private sealed class ParameterBucket
    {
        private readonly HashSet<long> categoryIds = [];
        private int writableSampleCount;
        private int readOnlySampleCount;

        public ParameterBucket(ParameterReference reference)
        {
            Reference = reference;
        }

        public ParameterReference Reference { get; }

        public void AddSample(long categoryId, bool isReadOnly)
        {
            categoryIds.Add(categoryId);
            if (isReadOnly)
            {
                readOnlySampleCount++;
            }
            else
            {
                writableSampleCount++;
            }
        }

        public ParameterCatalogItem ToCatalogItem()
        {
            return new ParameterCatalogItem(
                Reference,
                categoryIds,
                writableSampleCount + readOnlySampleCount,
                writableSampleCount,
                readOnlySampleCount);
        }
    }
}
