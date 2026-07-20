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
        new ParameterCategoryReference((long)BuiltInCategory.OST_Floors, "Перекрытия"),
        new ParameterCategoryReference((long)BuiltInCategory.OST_Ceilings, "Потолки"));

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
        CollectCategory(document, BuiltInCategory.OST_Ceilings, TargetCategories.Ceilings, buckets);

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
                ParameterReference? reference = RevitParameterReferenceFactory.Create(
                    parameter,
                    bindingKind);
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
