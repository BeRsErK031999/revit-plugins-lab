namespace TrueBIM.App.Modules.FinishSchedule.Models;

public sealed record ParameterCategoryReference
{
    public ParameterCategoryReference(long id, string name)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name)
            ? id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : name.Trim();
    }

    public long Id { get; }

    public string Name { get; }
}

public sealed record FinishScheduleParameterCategories
{
    public FinishScheduleParameterCategories(
        ParameterCategoryReference rooms,
        ParameterCategoryReference walls,
        ParameterCategoryReference floors)
        : this(rooms, walls, floors, floors)
    {
    }

    public FinishScheduleParameterCategories(
        ParameterCategoryReference rooms,
        ParameterCategoryReference walls,
        ParameterCategoryReference floors,
        ParameterCategoryReference ceilings)
    {
        Rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        Walls = walls ?? throw new ArgumentNullException(nameof(walls));
        Floors = floors ?? throw new ArgumentNullException(nameof(floors));
        Ceilings = ceilings ?? throw new ArgumentNullException(nameof(ceilings));
    }

    public ParameterCategoryReference Rooms { get; }

    public ParameterCategoryReference Walls { get; }

    public ParameterCategoryReference Floors { get; }

    public ParameterCategoryReference Ceilings { get; }
}

public sealed class ParameterCatalogItem
{
    public ParameterCatalogItem(
        ParameterReference reference,
        IEnumerable<long> categoryIds,
        int sampleCount,
        int writableSampleCount,
        int readOnlySampleCount)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        CategoryIds = (categoryIds ?? throw new ArgumentNullException(nameof(categoryIds)))
            .Distinct()
            .OrderBy(categoryId => categoryId)
            .ToArray();
        if (sampleCount < 0 || writableSampleCount < 0 || readOnlySampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        if (writableSampleCount + readOnlySampleCount != sampleCount)
        {
            throw new ArgumentException("Writable and read-only sample counts must match the total sample count.");
        }

        SampleCount = sampleCount;
        WritableSampleCount = writableSampleCount;
        ReadOnlySampleCount = readOnlySampleCount;
    }

    public ParameterReference Reference { get; }

    public IReadOnlyList<long> CategoryIds { get; }

    public int SampleCount { get; }

    public int WritableSampleCount { get; }

    public int ReadOnlySampleCount { get; }

    public bool IsWritableForAllSamples => SampleCount > 0 && WritableSampleCount == SampleCount;

    public bool SupportsCategory(long categoryId)
    {
        return CategoryIds.Contains(categoryId);
    }
}

public sealed class ParameterCatalog
{
    private readonly IReadOnlyDictionary<string, ParameterCatalogItem> itemsByStableKey;

    public ParameterCatalog(IEnumerable<ParameterCatalogItem> items)
    {
        Items = (items ?? throw new ArgumentNullException(nameof(items)))
            .OrderBy(item => item.Reference.BindingKind)
            .ThenBy(item => item.Reference.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Reference.StableKey, StringComparer.Ordinal)
            .ToArray();
        itemsByStableKey = Items.ToDictionary(
            item => item.Reference.StableKey,
            item => item,
            StringComparer.Ordinal);
    }

    public IReadOnlyList<ParameterCatalogItem> Items { get; }

    public ParameterCatalogItem? Find(ParameterReference reference)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        return itemsByStableKey.TryGetValue(reference.StableKey, out ParameterCatalogItem? item)
            ? item
            : null;
    }
}

public sealed class ParameterCatalogRequirement
{
    public ParameterCatalogRequirement(
        string roleName,
        ParameterBindingKind bindingKind,
        IEnumerable<ParameterStorageKind> allowedStorageKinds,
        IEnumerable<ParameterCategoryReference> requiredCategories,
        bool requireWritable)
    {
        RoleName = string.IsNullOrWhiteSpace(roleName) ? "Параметр" : roleName.Trim();
        BindingKind = bindingKind;
        AllowedStorageKinds = (allowedStorageKinds ?? throw new ArgumentNullException(nameof(allowedStorageKinds)))
            .Distinct()
            .ToArray();
        RequiredCategories = (requiredCategories ?? throw new ArgumentNullException(nameof(requiredCategories)))
            .GroupBy(category => category.Id)
            .Select(group => group.First())
            .ToArray();
        RequireWritable = requireWritable;
    }

    public string RoleName { get; }

    public ParameterBindingKind BindingKind { get; }

    public IReadOnlyList<ParameterStorageKind> AllowedStorageKinds { get; }

    public IReadOnlyList<ParameterCategoryReference> RequiredCategories { get; }

    public bool RequireWritable { get; }
}

public sealed record ParameterCompatibilityIssue(string Code, string Message);

public sealed class ParameterCompatibilityResult
{
    public ParameterCompatibilityResult(IEnumerable<ParameterCompatibilityIssue> issues)
    {
        Issues = (issues ?? throw new ArgumentNullException(nameof(issues))).ToArray();
    }

    public IReadOnlyList<ParameterCompatibilityIssue> Issues { get; }

    public bool IsCompatible => Issues.Count == 0;
}
