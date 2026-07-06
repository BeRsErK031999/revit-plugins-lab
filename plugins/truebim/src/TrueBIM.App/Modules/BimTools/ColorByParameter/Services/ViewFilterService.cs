using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ColorByParameter.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;
using RevitColor = Autodesk.Revit.DB.Color;

namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public sealed class ViewFilterService
{
    private const double DoubleTolerance = 0.000000001;
    private readonly FilterNameBuilder filterNameBuilder;
    private readonly ITrueBimLogger logger;

    public ViewFilterService(FilterNameBuilder filterNameBuilder, ITrueBimLogger logger)
    {
        this.filterNameBuilder = filterNameBuilder ?? throw new ArgumentNullException(nameof(filterNameBuilder));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ColorApplyResult Apply(
        Document document,
        View activeView,
        IReadOnlyList<BimCategoryItem> categories,
        BimParameterItem parameter,
        IReadOnlyList<ColorRuleRow> rows)
    {
        List<ColorRuleRow> selectedRows = rows.Where(row => row.IsSelected).ToList();
        if (selectedRows.Count == 0)
        {
            return new ColorApplyResult(0, 0, 0, rows.Count, 0, ["Не выбрано ни одно значение."]);
        }

        ICollection<ElementId> categoryIds = categories
            .Where(category => category.IsSelected)
            .Select(category => category.CategoryId)
            .ToList();
        if (categoryIds.Count == 0)
        {
            return new ColorApplyResult(0, 0, 0, selectedRows.Count, 0, ["Не выбрана ни одна категория."]);
        }

        FillPatternElement? solidFillPattern = FindSolidFillPattern(document);
        int created = 0;
        int updated = 0;
        int applied = 0;
        int skipped = 0;
        List<string> messages = [];

        using Transaction transaction = new(document, "TrueBIM: цвета по параметрам");
        transaction.Start();
        foreach (ColorRuleRow row in selectedRows)
        {
            if (!TryCreateRule(parameter, row.Value, out FilterRule? rule, out string? reason))
            {
                skipped++;
                messages.Add($"{row.DisplayValue}: {reason}");
                continue;
            }

            ElementParameterFilter elementFilter = new(rule);
            ISet<ElementId> categorySet = new HashSet<ElementId>(categoryIds);
            if (!ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(document, categorySet, elementFilter))
            {
                skipped++;
                messages.Add($"{row.DisplayValue}: выбранный параметр недоступен для части выбранных категорий.");
                continue;
            }

            string filterName = filterNameBuilder.Build(parameter.Name, row.DisplayValue);
            ParameterFilterElement? filter = FindParameterFilterByName(document, filterName);
            if (filter is null)
            {
                filter = ParameterFilterElement.Create(document, filterName, categoryIds, elementFilter);
                created++;
            }
            else
            {
                filter.SetCategories(categoryIds);
                if (!filter.SetElementFilter(elementFilter))
                {
                    skipped++;
                    messages.Add($"{row.DisplayValue}: Revit не принял правило фильтра.");
                    continue;
                }

                updated++;
            }

            if (!activeView.GetFilters().Contains(filter.Id))
            {
                activeView.AddFilter(filter.Id);
            }

            activeView.SetFilterOverrides(filter.Id, CreateOverrides(row, solidFillPattern));
            activeView.SetFilterVisibility(filter.Id, true);
            applied++;
        }

        transaction.Commit();

        logger.Info($"Color By Parameter applied {applied} filters. Created={created}, Updated={updated}, Skipped={skipped}.");
        return new ColorApplyResult(created, updated, applied, skipped, 0, messages);
    }

    public ColorApplyResult ClearOwnedFiltersFromView(Document document, View activeView)
    {
        List<ElementId> filtersToRemove = activeView.GetFilters()
            .Where(filterId => document.GetElement(filterId) is ParameterFilterElement filter
                && filterNameBuilder.IsOwnedFilterName(filter.Name))
            .ToList();

        if (filtersToRemove.Count == 0)
        {
            return new ColorApplyResult(0, 0, 0, 0, 0, ["На активном виде нет фильтров TrueBIM для очистки."]);
        }

        using Transaction transaction = new(document, "TrueBIM: очистить цвета по параметрам");
        transaction.Start();
        foreach (ElementId filterId in filtersToRemove)
        {
            activeView.RemoveFilter(filterId);
        }

        transaction.Commit();

        logger.Info($"Color By Parameter removed {filtersToRemove.Count} filters from active view.");
        return new ColorApplyResult(0, 0, 0, 0, filtersToRemove.Count, []);
    }

    private static bool TryCreateRule(BimParameterItem parameter, ParameterValueToken value, out FilterRule? rule, out string? reason)
    {
        rule = null;
        reason = null;

        try
        {
            if (value.IsEmpty)
            {
#if REVIT2022_OR_GREATER
                rule = ParameterFilterRuleFactory.CreateHasNoValueParameterRule(parameter.ParameterId);
                return true;
#else
                if (parameter.StorageType == StorageType.String)
                {
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.ParameterId, string.Empty, false);
                    return true;
                }

                reason = "пустые значения для этого типа параметра поддержаны только в Revit 2022+.";
                return false;
#endif
            }

            switch (parameter.StorageType)
            {
                case StorageType.String when value.StringValue is not null:
#if REVIT2024_OR_GREATER
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.ParameterId, value.StringValue);
#else
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.ParameterId, value.StringValue, false);
#endif
                    return true;
                case StorageType.Integer when value.IntegerValue.HasValue:
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.ParameterId, value.IntegerValue.Value);
                    return true;
                case StorageType.Double when value.DoubleValue.HasValue:
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.ParameterId, value.DoubleValue.Value, DoubleTolerance);
                    return true;
                case StorageType.ElementId when value.ElementIdValue.HasValue:
                    rule = ParameterFilterRuleFactory.CreateEqualsRule(parameter.ParameterId, RevitElementIds.Create(value.ElementIdValue.Value));
                    return true;
                default:
                    reason = "тип значения не соответствует типу параметра.";
                    return false;
            }
        }
        catch (Exception exception)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static ParameterFilterElement? FindParameterFilterByName(Document document, string filterName)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .FirstOrDefault(filter => string.Equals(filter.Name, filterName, StringComparison.Ordinal));
    }

    private static OverrideGraphicSettings CreateOverrides(ColorRuleRow row, FillPatternElement? solidFillPattern)
    {
        RevitColor color = new(row.Red, row.Green, row.Blue);
        OverrideGraphicSettings settings = new();
        settings.SetProjectionLineColor(color);
        settings.SetSurfaceForegroundPatternColor(color);
        settings.SetSurfaceForegroundPatternVisible(true);

        if (solidFillPattern is not null)
        {
            settings.SetSurfaceForegroundPatternId(solidFillPattern.Id);
        }

        return settings;
    }

    private static FillPatternElement? FindSolidFillPattern(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(pattern => pattern.GetFillPattern().IsSolidFill);
    }
}
