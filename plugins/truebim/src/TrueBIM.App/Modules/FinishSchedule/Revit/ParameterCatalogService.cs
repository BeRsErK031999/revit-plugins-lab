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
        CollectTextBindings(document, buckets);

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
        foreach (Element instance in new FilteredElementCollector(document)
                     .OfCategory(builtInCategory).WhereElementIsNotElementType())
        {
            CollectElement(instance, ParameterBindingKind.Instance, category, buckets);
        }

        foreach (Element type in new FilteredElementCollector(document)
                     .OfCategory(builtInCategory).WhereElementIsElementType())
        {
            CollectElement(type, ParameterBindingKind.Type, category, buckets);
        }
    }

    private static void CollectTextBindings(Document document, Dictionary<string, ParameterBucket> buckets)
    {
        using DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition
                || iterator.Current is not ElementBinding binding
                || !IsStringDefinition(definition))
            {
                continue;
            }

            ParameterBindingKind kind = binding is TypeBinding ? ParameterBindingKind.Type : ParameterBindingKind.Instance;
            long id = RevitElementIds.GetValue(definition.Id);
            ParameterReference reference = document.GetElement(definition.Id) is SharedParameterElement shared
                ? ParameterReference.Shared(definition.Name, shared.GuidValue, id, kind, ParameterStorageKind.String)
                : ParameterReference.Project(definition.Name, id, kind, ParameterStorageKind.String);
            foreach (Category category in binding.Categories)
            {
                long categoryId = RevitElementIds.GetValue(category.Id);
                if (categoryId == TargetCategories.Rooms.Id || categoryId == TargetCategories.Walls.Id
                    || categoryId == TargetCategories.Floors.Id || categoryId == TargetCategories.Ceilings.Id)
                {
                    GetBucket(buckets, reference).AddBinding(categoryId);
                }
            }
        }
    }

    private static bool IsStringDefinition(Definition definition)
    {
#if REVIT2022_OR_GREATER
        ForgeTypeId type = definition.GetDataType();
        return type == SpecTypeId.String.Text || type == SpecTypeId.String.MultilineText || type == SpecTypeId.String.Url;
#else
        return definition.ParameterType is ParameterType.Text or ParameterType.MultilineText or ParameterType.URL;
#endif
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

                GetBucket(buckets, reference).AddSample(category.Id, parameter.IsReadOnly);
            }
        }
        catch (Exception exception)
        {
            logger.Warning(
                $"Finish Schedule skipped parameters of element {RevitElementIds.GetValue(element.Id)}: {exception.Message}");
        }
    }

    private static ParameterBucket GetBucket(Dictionary<string, ParameterBucket> buckets, ParameterReference reference)
    {
        if (!buckets.TryGetValue(reference.StableKey, out ParameterBucket? bucket))
        {
            bucket = new ParameterBucket(reference);
            buckets.Add(reference.StableKey, bucket);
        }
        return bucket;
    }

    private sealed class ParameterBucket
    {
        private readonly HashSet<long> categoryIds = [];
        private int writableSampleCount;
        private int readOnlySampleCount;
        private bool hasProjectBinding;

        public ParameterBucket(ParameterReference reference)
        {
            Reference = reference;
        }

        public ParameterReference Reference { get; }

        public void AddBinding(long categoryId)
        {
            categoryIds.Add(categoryId);
            hasProjectBinding = true;
        }

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
                readOnlySampleCount,
                hasProjectBinding);
        }
    }
}
