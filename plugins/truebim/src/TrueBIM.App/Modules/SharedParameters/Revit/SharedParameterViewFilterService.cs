using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public sealed class SharedParameterViewFilterService
{
    private readonly ISharedParameterVersionAdapter versionAdapter;

    public SharedParameterViewFilterService(ISharedParameterVersionAdapter versionAdapter)
    {
        this.versionAdapter = versionAdapter ?? throw new ArgumentNullException(nameof(versionAdapter));
    }

    public ViewFilterUsage? Analyze(
        Document document,
        ParameterFilterElement filterElement,
        long targetParameterId,
        IReadOnlyDictionary<long, IReadOnlyList<AppliedViewFilterUsage>> applications)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(filterElement, nameof(filterElement));

        ElementFilter elementFilter;
        try
        {
            elementFilter = filterElement.GetElementFilter();
        }
        catch
        {
            ISet<ElementId> parameters = filterElement.GetElementFilterParameters();
            if (!parameters.Any(id => RevitElementIds.GetValue(id) == targetParameterId))
            {
                return null;
            }

            return new ViewFilterUsage(
                RevitElementIds.GetValue(filterElement.Id),
                filterElement.Name,
                CollectCategories(document, filterElement),
                new FilterTreeNodeDescriptor("Unsupported", [], [], DetectionConfidence.Partial),
                [new FilterRuleDescriptor(
                    targetParameterId,
                    document.GetElement(RevitElementIds.Create(targetParameterId))?.Name ?? targetParameterId.ToString(),
                    "Unknown",
                    string.Empty,
                    "Unknown",
                    false,
                    DetectionConfidence.Partial,
                    "Unknown")],
                [],
                GetAppliedViews(applications, RevitElementIds.GetValue(filterElement.Id)),
                false,
                DetectionConfidence.Partial);
        }

        IReadOnlyList<FilterRuleDescriptor> rules = versionAdapter.ExtractFilterRules(
            document,
            elementFilter,
            targetParameterId);
        List<FilterRuleDescriptor> targetRules = rules
            .Where(rule => rule.ParameterElementId == targetParameterId)
            .ToList();
        if (targetRules.Count == 0)
        {
            return null;
        }

        long filterId = RevitElementIds.GetValue(filterElement.Id);
        bool canRebuild = TryBuildFilterWithoutTarget(elementFilter, targetParameterId, out _);
        return new ViewFilterUsage(
            filterId,
            filterElement.Name,
            CollectCategories(document, filterElement),
            BuildTree(document, elementFilter, targetParameterId),
            targetRules,
            rules.Where(rule => rule.ParameterElementId != targetParameterId).ToList(),
            GetAppliedViews(applications, filterId),
            canRebuild,
            rules.Any(rule => rule.Confidence != DetectionConfidence.Exact)
                ? DetectionConfidence.Partial
                : DetectionConfidence.Exact);
    }

    private static IReadOnlyList<AppliedViewFilterUsage> GetAppliedViews(
        IReadOnlyDictionary<long, IReadOnlyList<AppliedViewFilterUsage>> applications,
        long filterId)
    {
        return applications.TryGetValue(filterId, out IReadOnlyList<AppliedViewFilterUsage>? views)
            ? views
            : [];
    }

    public bool TryBuildFilterWithoutTarget(
        ElementFilter source,
        long targetParameterId,
        out ElementFilter? rebuilt)
    {
        Guard.NotNull(source, nameof(source));

        if (source is ElementParameterFilter parameterFilter)
        {
            List<FilterRule> remainingRules = parameterFilter
                .GetRules()
                .Where(rule => RevitElementIds.GetValue(rule.GetRuleParameter()) != targetParameterId)
                .ToList();
            if (remainingRules.Count == 0)
            {
                rebuilt = null;
                return true;
            }

            rebuilt = new ElementParameterFilter(remainingRules, parameterFilter.Inverted);
            return true;
        }

        if (source is not ElementLogicalFilter logicalFilter || logicalFilter.Inverted)
        {
            rebuilt = null;
            return false;
        }

        List<ElementFilter> remainingChildren = [];
        foreach (ElementFilter child in logicalFilter.GetFilters())
        {
            if (!TryBuildFilterWithoutTarget(child, targetParameterId, out ElementFilter? rebuiltChild))
            {
                rebuilt = null;
                return false;
            }

            if (rebuiltChild is not null)
            {
                remainingChildren.Add(rebuiltChild);
            }
        }

        if (remainingChildren.Count == 0)
        {
            rebuilt = null;
            return true;
        }

        if (remainingChildren.Count == 1)
        {
            rebuilt = remainingChildren[0];
            return true;
        }

        rebuilt = logicalFilter switch
        {
            LogicalAndFilter => new LogicalAndFilter(remainingChildren),
            LogicalOrFilter => new LogicalOrFilter(remainingChildren),
            _ => null
        };
        return rebuilt is not null;
    }

    private FilterTreeNodeDescriptor BuildTree(
        Document document,
        ElementFilter filter,
        long targetParameterId)
    {
        if (filter is ElementLogicalFilter logicalFilter)
        {
            IReadOnlyList<FilterTreeNodeDescriptor> children = logicalFilter
                .GetFilters()
                .Select(child => BuildTree(document, child, targetParameterId))
                .ToList();
            return new FilterTreeNodeDescriptor(
                logicalFilter is LogicalAndFilter ? "AND" : "OR",
                children,
                [],
                children.Any(child => child.Confidence != DetectionConfidence.Exact)
                    ? DetectionConfidence.Partial
                    : DetectionConfidence.Exact);
        }

        if (filter is ElementParameterFilter)
        {
            IReadOnlyList<FilterRuleDescriptor> rules = versionAdapter.ExtractFilterRules(
                document,
                filter,
                targetParameterId);
            return new FilterTreeNodeDescriptor(
                filter.Inverted ? "NOT" : "RULES",
                [],
                rules,
                rules.Any(rule => rule.Confidence != DetectionConfidence.Exact)
                    ? DetectionConfidence.Partial
                    : DetectionConfidence.Exact);
        }

        return new FilterTreeNodeDescriptor(
            filter.GetType().Name,
            [],
            [],
            DetectionConfidence.Unsupported);
    }

    private static IReadOnlyList<CategoryDescriptor> CollectCategories(
        Document document,
        ParameterFilterElement filterElement)
    {
        return filterElement.GetCategories()
            .Select(id => Category.GetCategory(document, id))
            .Where(category => category is not null)
            .Select(category => new CategoryDescriptor(
                RevitElementIds.GetValue(category.Id),
                category.Name))
            .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
