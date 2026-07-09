using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Modules.BimTools.CopyParameters.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public sealed class ColorByParameterService
{
    private readonly ViewFilterService viewFilterService;
    private readonly ColorPaletteService colorPaletteService;
    private readonly ITrueBimLogger logger;

    public ColorByParameterService(
        ViewFilterService viewFilterService,
        ColorPaletteService colorPaletteService,
        ITrueBimLogger logger)
    {
        this.viewFilterService = viewFilterService ?? throw new ArgumentNullException(nameof(viewFilterService));
        this.colorPaletteService = colorPaletteService ?? throw new ArgumentNullException(nameof(colorPaletteService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<BimCategoryItem> CollectCategories(Document document, View activeView)
    {
        Dictionary<long, CategoryBucket> buckets = [];
        foreach (Element element in CollectVisibleElements(document, activeView))
        {
            Category? category = element.Category;
            if (!CanUseCategory(category))
            {
                continue;
            }

            long categoryId = RevitElementIds.GetValue(category.Id);
            if (!buckets.TryGetValue(categoryId, out CategoryBucket? bucket))
            {
                bucket = new CategoryBucket(category.Id, category.Name);
                buckets.Add(categoryId, bucket);
            }

            bucket.ElementCount++;
        }

        return buckets.Values
            .OrderBy(bucket => bucket.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(bucket => new BimCategoryItem(bucket.CategoryId, bucket.Name, bucket.ElementCount))
            .ToList();
    }

    public IReadOnlyList<BimParameterItem> CollectParameters(Document document, View activeView, IReadOnlyList<BimCategoryItem> categories)
    {
        HashSet<long> selectedCategoryIds = CreateCategoryIdSet(categories);
        ProjectParameterBindingIndex projectParameterBindings = ProjectParameterBindingIndex.Create(document);
        Dictionary<ParameterKey, ParameterBucket> buckets = [];

        foreach (Element element in CollectVisibleElements(document, activeView))
        {
            if (!IsElementInCategories(element, selectedCategoryIds))
            {
                continue;
            }

            AddParameters(element, ParameterSourceKind.Instance, projectParameterBindings, buckets);

            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                Element? typeElement = document.GetElement(typeId);
                if (typeElement is not null)
                {
                    AddParameters(typeElement, ParameterSourceKind.Type, projectParameterBindings, buckets);
                }
            }
        }

        return buckets.Values
            .OrderBy(bucket => bucket.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(bucket => bucket.SourceKind)
            .Select(bucket => new BimParameterItem(
                bucket.ParameterId,
                bucket.Name,
                bucket.StorageType,
                bucket.SourceKind,
                bucket.ElementIds.Count,
                bucket.CategoryIds.Count))
            .ToList();
    }

    public ColorValueCollection CollectValues(
        Document document,
        View activeView,
        IReadOnlyList<BimCategoryItem> categories,
        BimParameterItem parameter,
        int maxValueCount)
    {
        HashSet<long> selectedCategoryIds = CreateCategoryIdSet(categories);
        Dictionary<ParameterValueToken, int> valueCounts = [];

        foreach (Element element in CollectVisibleElements(document, activeView))
        {
            if (!IsElementInCategories(element, selectedCategoryIds))
            {
                continue;
            }

            Parameter? sourceParameter = FindParameter(document, element, parameter);
            ParameterValueToken token = sourceParameter is null
                ? ParameterValueToken.Empty()
                : CreateValueToken(sourceParameter);
            valueCounts[token] = valueCounts.TryGetValue(token, out int count) ? count + 1 : 1;
        }

        List<ParameterValueToken> values = valueCounts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key.DisplayValue, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => pair.Key)
            .ToList();
        int totalValueCount = values.Count;
        if (maxValueCount > 0 && values.Count > maxValueCount)
        {
            values = values.Take(maxValueCount).ToList();
            logger.Warning($"Color By Parameter truncated value list to {maxValueCount} of {totalValueCount} values for '{parameter.Name}'.");
        }

        IReadOnlyList<ColorSwatch> colors = colorPaletteService.Generate(values.Count);
        List<ColorRuleRow> rows = [];
        for (int index = 0; index < values.Count; index++)
        {
            rows.Add(new ColorRuleRow(values[index], colors[index]));
        }

        return new ColorValueCollection(rows, totalValueCount);
    }

    public void AssignColors(IReadOnlyList<ColorRuleRow> rows, int paletteOffset = 0)
    {
        IReadOnlyList<ColorSwatch> colors = colorPaletteService.Generate(rows.Count, paletteOffset);
        for (int index = 0; index < rows.Count; index++)
        {
            rows[index].SetColor(colors[index]);
        }
    }

    public ColorApplyResult Apply(
        Document document,
        View activeView,
        IReadOnlyList<BimCategoryItem> categories,
        BimParameterItem parameter,
        IReadOnlyList<ColorRuleRow> rows)
    {
        return viewFilterService.Apply(document, activeView, categories, parameter, rows);
    }

    public ColorApplyResult Clear(Document document, View activeView)
    {
        return viewFilterService.ClearOwnedFiltersFromView(document, activeView);
    }

    private static IEnumerable<Element> CollectVisibleElements(Document document, View activeView)
    {
        return new FilteredElementCollector(document, activeView.Id)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(element => element.Category is not null);
    }

    private static bool CanUseCategory(Category? category)
    {
        return category is not null
            && category.Id != ElementId.InvalidElementId
            && category.AllowsBoundParameters
            && category.CategoryType != CategoryType.Internal;
    }

    private static HashSet<long> CreateCategoryIdSet(IReadOnlyList<BimCategoryItem> categories)
    {
        return categories
            .Where(category => category.IsSelected)
            .Select(category => RevitElementIds.GetValue(category.CategoryId))
            .ToHashSet();
    }

    private static bool IsElementInCategories(Element element, HashSet<long> selectedCategoryIds)
    {
        return element.Category is not null
            && selectedCategoryIds.Contains(RevitElementIds.GetValue(element.Category.Id));
    }

    private static void AddParameters(
        Element element,
        ParameterSourceKind sourceKind,
        ProjectParameterBindingIndex projectParameterBindings,
        Dictionary<ParameterKey, ParameterBucket> buckets)
    {
        Category? category = element.Category;
        long categoryId = category is null ? 0 : RevitElementIds.GetValue(category.Id);
        foreach (Parameter parameter in element.Parameters)
        {
            if (!IsSupportedParameter(parameter))
            {
                continue;
            }

            string name = parameter.Definition?.Name ?? string.Empty;
            if (!projectParameterBindings.Contains(name, sourceKind, categoryId))
            {
                continue;
            }

            ParameterKey key = new(RevitElementIds.GetValue(parameter.Id), parameter.StorageType, sourceKind, name);
            if (!buckets.TryGetValue(key, out ParameterBucket? bucket))
            {
                bucket = new ParameterBucket(parameter.Id, name, parameter.StorageType, sourceKind);
                buckets.Add(key, bucket);
            }

            bucket.ElementIds.Add(RevitElementIds.GetValue(element.Id));
            if (category is not null)
            {
                bucket.CategoryIds.Add(categoryId);
            }
        }
    }

    private static bool IsSupportedParameter(Parameter parameter)
    {
        return parameter.Definition is not null
            && parameter.Id != ElementId.InvalidElementId
            && parameter.StorageType is StorageType.String or StorageType.Integer or StorageType.Double or StorageType.ElementId;
    }

    private static Parameter? FindParameter(Document document, Element element, BimParameterItem parameter)
    {
        Element sourceElement = element;
        if (parameter.SourceKind == ParameterSourceKind.Type)
        {
            ElementId typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                return null;
            }

            Element? typeElement = document.GetElement(typeId);
            if (typeElement is null)
            {
                return null;
            }

            sourceElement = typeElement;
        }

        foreach (Parameter candidate in sourceElement.Parameters)
        {
            if (candidate.StorageType == parameter.StorageType
                && RevitElementIds.GetValue(candidate.Id) == RevitElementIds.GetValue(parameter.ParameterId)
                && string.Equals(candidate.Definition?.Name, parameter.Name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static ParameterValueToken CreateValueToken(Parameter parameter)
    {
        if (!parameter.HasValue)
        {
            return ParameterValueToken.Empty();
        }

        return parameter.StorageType switch
        {
            StorageType.String => CreateStringValue(parameter),
            StorageType.Integer => ParameterValueToken.FromInteger(
                parameter.AsInteger(),
                GetDisplayValue(parameter, parameter.AsInteger().ToString(System.Globalization.CultureInfo.CurrentCulture))),
            StorageType.Double => ParameterValueToken.FromDouble(
                parameter.AsDouble(),
                GetDisplayValue(parameter, parameter.AsDouble().ToString("G", System.Globalization.CultureInfo.CurrentCulture))),
            StorageType.ElementId => CreateElementIdValue(parameter),
            _ => ParameterValueToken.Empty()
        };
    }

    private static ParameterValueToken CreateStringValue(Parameter parameter)
    {
        string? value = parameter.AsString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return ParameterValueToken.Empty();
        }

        return ParameterValueToken.FromString(value, GetDisplayValue(parameter, value));
    }

    private static ParameterValueToken CreateElementIdValue(Parameter parameter)
    {
        ElementId value = parameter.AsElementId();
        if (value == ElementId.InvalidElementId)
        {
            return ParameterValueToken.Empty();
        }

        return ParameterValueToken.FromElementId(
            RevitElementIds.GetValue(value),
            GetDisplayValue(parameter, RevitElementIds.GetValue(value).ToString(System.Globalization.CultureInfo.CurrentCulture)));
    }

    private static string GetDisplayValue(Parameter parameter, string fallback)
    {
        string? valueString = parameter.AsValueString();
        return string.IsNullOrWhiteSpace(valueString) ? fallback : valueString;
    }

    private sealed record CategoryBucket(ElementId CategoryId, string Name)
    {
        public int ElementCount { get; set; }
    }

    private sealed record ParameterKey(long ParameterId, StorageType StorageType, ParameterSourceKind SourceKind, string Name);

    private sealed class ProjectParameterBindingIndex
    {
        private readonly Dictionary<BindingKey, HashSet<long>> categoryIdsByBinding;

        private ProjectParameterBindingIndex(Dictionary<BindingKey, HashSet<long>> categoryIdsByBinding)
        {
            this.categoryIdsByBinding = categoryIdsByBinding;
        }

        public static ProjectParameterBindingIndex Create(Document document)
        {
            Dictionary<BindingKey, HashSet<long>> bindings = [];
            DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
            iterator.Reset();
            while (iterator.MoveNext())
            {
                Definition definition = iterator.Key;
                Binding binding = (Binding)iterator.Current;
                if (binding is not ElementBinding elementBinding)
                {
                    continue;
                }

                ParameterSourceKind sourceKind = binding is TypeBinding
                    ? ParameterSourceKind.Type
                    : ParameterSourceKind.Instance;
                BindingKey key = new(NormalizeParameterName(definition.Name), sourceKind);
                if (!bindings.TryGetValue(key, out HashSet<long>? categoryIds))
                {
                    categoryIds = [];
                    bindings.Add(key, categoryIds);
                }

                foreach (Category category in elementBinding.Categories)
                {
                    categoryIds.Add(RevitElementIds.GetValue(category.Id));
                }
            }

            return new ProjectParameterBindingIndex(bindings);
        }

        public bool Contains(string name, ParameterSourceKind sourceKind, long categoryId)
        {
            return categoryIdsByBinding.TryGetValue(new BindingKey(NormalizeParameterName(name), sourceKind), out HashSet<long>? categoryIds)
                && categoryIds.Contains(categoryId);
        }

        private static string NormalizeParameterName(string name)
        {
            return name.Trim().ToUpperInvariant();
        }
    }

    private sealed record BindingKey(string Name, ParameterSourceKind SourceKind);

    private sealed class ParameterBucket
    {
        public ParameterBucket(ElementId parameterId, string name, StorageType storageType, ParameterSourceKind sourceKind)
        {
            ParameterId = parameterId;
            Name = name;
            StorageType = storageType;
            SourceKind = sourceKind;
        }

        public ElementId ParameterId { get; }

        public string Name { get; }

        public StorageType StorageType { get; }

        public ParameterSourceKind SourceKind { get; }

        public HashSet<long> ElementIds { get; } = [];

        public HashSet<long> CategoryIds { get; } = [];
    }
}
