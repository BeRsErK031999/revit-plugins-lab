using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Modules.SharedParameters.Services;

public sealed class SharedParameterUsageSummaryBuilder
{
    public IReadOnlyList<ElementUsageAggregate> BuildElementAggregates(
        IReadOnlyList<ElementParameterUsage> usages)
    {
        if (usages is null)
        {
            throw new ArgumentNullException(nameof(usages));
        }

        return usages
            .GroupBy(usage => string.IsNullOrWhiteSpace(usage.CategoryName) ? "Без категории" : usage.CategoryName)
            .Select(group => new ElementUsageAggregate(
                group.Key,
                group.Count(),
                group.Count(usage => usage.HasParameter),
                group.Count(usage => usage.HasParameter && usage.HasValue),
                group.Count(usage => usage.HasParameter && !usage.HasValue),
                group.Count(usage => usage.HasParameter && usage.IsReadOnly)))
            .OrderBy(aggregate => aggregate.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<ElementParameterUsage> DeduplicateElements(
        IReadOnlyList<ElementParameterUsage> usages)
    {
        if (usages is null)
        {
            throw new ArgumentNullException(nameof(usages));
        }

        return usages
            .GroupBy(usage => usage.ElementId)
            .Select(group => group.First())
            .OrderBy(usage => usage.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(usage => usage.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(usage => usage.ElementId)
            .ToList();
    }
}
