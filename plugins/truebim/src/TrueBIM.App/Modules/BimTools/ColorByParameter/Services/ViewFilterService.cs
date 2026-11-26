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
        IReadOnlyList<ColorRuleRow> rows,
        bool useTemporaryViewProperties,
        bool colorPatterns = true,
        bool colorLines = false)
    {
        List<ColorRuleRow> selectedRows = rows.Where(row => row.IsSelected).ToList();
        if (!colorPatterns && !colorLines)
        {
            return new ColorApplyResult(0, 0, 0, selectedRows.Count, 0, ["Выберите штриховки или линии для раскраски."]);
        }
        if (selectedRows.Count == 0)
        {
            return new ColorApplyResult(0, 0, 0, rows.Count, 0, ["Не выбрано ни одно значение."]);
        }

        List<ElementId> selectedCategoryIds = categories
            .Where(category => category.IsSelected)
            .Select(category => category.CategoryId)
            .ToList();
        if (selectedCategoryIds.Count == 0)
        {
            return new ColorApplyResult(0, 0, 0, selectedRows.Count, 0, ["Не выбрана ни одна категория."]);
        }

        List<ElementId> categoryIds = GetApplicableCategoryIds(document, selectedCategoryIds, parameter);
        if (categoryIds.Count == 0)
        {
            return new ColorApplyResult(0, 0, 0, selectedRows.Count, 0, ["Выбранный параметр не назначен выбранным категориям. Обновите значения или выберите другие категории."]);
        }

        bool temporaryViewPropertiesEnabled = activeView.IsTemporaryViewPropertiesModeEnabled();
        if (!useTemporaryViewProperties && temporaryViewPropertiesEnabled)
        {
            return new ColorApplyResult(
                0,
                0,
                0,
                selectedRows.Count,
                0,
                ["На активном виде уже включены временные свойства. Включите флажок «На временном виде» или отключите режим временных свойств в Revit перед постоянным применением."]);
        }

        if (useTemporaryViewProperties
            && !temporaryViewPropertiesEnabled
            && !activeView.CanEnableTemporaryViewPropertiesMode())
        {
            return new ColorApplyResult(
                0,
                0,
                0,
                selectedRows.Count,
                0,
                ["Revit не разрешает включить временные свойства для активного вида. Снимите флажок «На временном виде» или выберите другой вид."]);
        }

        var duplicate = selectedRows.GroupBy(row => filterNameBuilder.Build(parameter.Name, row.DisplayValue),
            StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            return new ColorApplyResult(0, 0, 0, selectedRows.Count, 0,
                [$"Несколько значений дают имя «{duplicate.Key}». Выберите одно из них. Раскраска вида сохранена."]);
        }

        FillPatternElement? solidFillPattern = colorPatterns ? FindSolidFillPattern(document) : null;
        int created = 0;
        int updated = 0;
        int applied = 0;
        int skipped = 0;
        int replaced = 0;
        int deleted = 0;
        List<string> messages = [];
        HashSet<long>? otherViewFilters = null;
        int skippedCategoryCount = selectedCategoryIds.Count - categoryIds.Count;
        if (skippedCategoryCount > 0)
        {
            messages.Add($"Параметр '{parameter.Name}' применим не ко всем выбранным категориям; пропущено категорий: {skippedCategoryCount}.");
        }

        using Transaction transaction = new(document, "TrueBIM: цвета по параметрам");
        transaction.Start();

        // When the view is still in its permanent state, orphaned filters can be removed safely.
        // In an already active temporary mode GetFilters() does not expose the underlying permanent set.
        if (!temporaryViewPropertiesEnabled)
        {
            deleted += DeleteOrphanedOwnedFilters(document);
        }

        if (useTemporaryViewProperties && !temporaryViewPropertiesEnabled)
        {
            activeView.EnableTemporaryViewPropertiesMode(activeView.Id);
        }

        List<ElementId> filtersToReplace = GetOwnedFilterIds(document, activeView);
        foreach (ElementId filterId in filtersToReplace)
        {
            activeView.RemoveFilter(filterId);
        }

        replaced = filtersToReplace.Count;
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
            using SubTransaction subTransaction = new(document);
            try
            {
                if (subTransaction.Start() != TransactionStatus.Started)
                {
                    throw new InvalidOperationException("Revit не начал вложенную транзакцию фильтра.");
                }

                if (!ParameterFilterElement.ElementFilterIsAcceptableForParameterFilterElement(document, categorySet, elementFilter))
                {
                    subTransaction.RollBack();
                    skipped++;
                    messages.Add($"{row.DisplayValue}: выбранный параметр недоступен для выбранных категорий.");
                    continue;
                }

                string filterName = filterNameBuilder.Build(parameter.Name, row.DisplayValue);
                ParameterFilterElement? filter = FindParameterFilterByName(document, filterName);
                bool wasCreated = filter is null;
                if (filter is null)
                {
                    filter = ParameterFilterElement.Create(document, filterName, categoryIds, elementFilter);
                }
                else
                {
                    bool changeCategories = !categorySet.SetEquals(filter.GetCategories());
                    bool sharedCategories = changeCategories && (useTemporaryViewProperties
                        || (otherViewFilters ??= GetReferencedFilterIds(document, activeView)).Contains(RevitElementIds.GetValue(filter.Id)));
                    if (changeCategories && !sharedCategories)
                    {
                        filter.SetCategories(categoryIds);
                    }
                    // Revit returns false for equivalent rules. A changed rule would reuse a display name for a different value.
                    if (sharedCategories || filter.SetElementFilter(elementFilter))
                    {
                        subTransaction.RollBack();
                        transaction.RollBack();
                        return new ColorApplyResult(0, 0, 0, selectedRows.Count, 0,
                            [$"Имя «{filterName}» уже занято фильтром с другим правилом или набором категорий. Раскраска вида сохранена."]);
                    }
                }

                if (!activeView.GetFilters().Contains(filter.Id))
                {
                    activeView.AddFilter(filter.Id);
                }

                using OverrideGraphicSettings settings = CreateOverrides(row, solidFillPattern, colorPatterns, colorLines);
                activeView.SetFilterOverrides(filter.Id, settings);
                activeView.SetFilterVisibility(filter.Id, true);
                if (subTransaction.Commit() != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException("Revit откатил вложенную транзакцию фильтра.");
                }

                if (wasCreated)
                {
                    created++;
                }
                else
                {
                    updated++;
                }

                applied++;
            }
            catch (Exception exception)
            {
                if (subTransaction.GetStatus() == TransactionStatus.Started)
                {
                    subTransaction.RollBack();
                }

                skipped++;
                string message = $"{row.DisplayValue}: Revit отклонил фильтр для выбранных категорий ({exception.Message}).";
                messages.Add(message);
                logger.Warning(message);
            }
        }

        if (applied == 0)
        {
            transaction.RollBack();
            return new ColorApplyResult(0, 0, 0, skipped, 0, messages);
        }

        if (!useTemporaryViewProperties)
        {
            deleted += DeleteOrphanedOwnedFilters(document);
        }

        transaction.Commit();

        if (useTemporaryViewProperties)
        {
            messages.Insert(0, "Раскраска применена во временном режиме. После отключения временных свойств Revit вернет исходное оформление вида.");
        }

        if (deleted > 0)
        {
            messages.Add($"Удалено неиспользуемых фильтров раскраски TrueBIM из проекта: {deleted}.");
        }

        logger.Info($"Color By Parameter applied {applied} filters. Created={created}, Updated={updated}, Replaced={replaced}, Deleted={deleted}, Skipped={skipped}, Temporary={useTemporaryViewProperties}.");
        return new ColorApplyResult(created, updated, applied, skipped, replaced, messages);
    }

    public ColorApplyResult ClearOwnedFiltersFromView(Document document, View activeView)
    {
        List<ElementId> filtersToRemove = GetOwnedFilterIds(document, activeView);

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

        int deleted = 0;
        List<string> messages = [];
        if (activeView.IsTemporaryViewPropertiesModeEnabled())
        {
            messages.Add("Фильтры сняты только с временного оформления. Постоянные свойства вида не изменены.");
        }
        else
        {
            deleted = DeleteOrphanedOwnedFilters(document);
            if (deleted > 0)
            {
                messages.Add($"Удалено неиспользуемых фильтров раскраски TrueBIM из проекта: {deleted}.");
            }
        }

        transaction.Commit();

        logger.Info($"Color By Parameter removed {filtersToRemove.Count} filters from active view and deleted {deleted} orphaned filters.");
        return new ColorApplyResult(0, 0, 0, 0, filtersToRemove.Count, messages);
    }

    private List<ElementId> GetOwnedFilterIds(Document document, View view)
    {
        return view.GetFilters()
            .Where(filterId => document.GetElement(filterId) is ParameterFilterElement filter
                && filterNameBuilder.IsOwnedFilterName(filter.Name))
            .ToList();
    }

    private static HashSet<long> GetReferencedFilterIds(Document document, View? except = null)
    {
        HashSet<long> referencedFilterIds = [];
        foreach (View view in new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>())
        {
            if (view.Id == except?.Id) continue;
            try
            {
                foreach (ElementId filterId in view.GetFilters())
                {
                    referencedFilterIds.Add(RevitElementIds.GetValue(filterId));
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // Some view types do not support visibility/graphics filters.
            }
        }

        return referencedFilterIds;
    }

    private int DeleteOrphanedOwnedFilters(Document document)
    {
        HashSet<long> referencedFilterIds = GetReferencedFilterIds(document);
        List<ElementId> orphanedFilterIds = new FilteredElementCollector(document)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .Where(filter => filterNameBuilder.IsOwnedFilterName(filter.Name)
                && !referencedFilterIds.Contains(RevitElementIds.GetValue(filter.Id)))
            .Select(filter => filter.Id)
            .ToList();

        foreach (ElementId filterId in orphanedFilterIds)
        {
            document.Delete(filterId);
        }

        return orphanedFilterIds.Count;
    }

    private static List<ElementId> GetApplicableCategoryIds(
        Document document,
        IReadOnlyList<ElementId> selectedCategoryIds,
        BimParameterItem parameter)
    {
        IReadOnlyList<long> applicableCategoryIds = ApplicableCategoryFilter.GetApplicableCategoryIds(
            selectedCategoryIds.Select(RevitElementIds.GetValue),
            parameter.ApplicableCategoryIds);
        HashSet<long> applicableCategoryIdSet = applicableCategoryIds.ToHashSet();
        long parameterId = RevitElementIds.GetValue(parameter.ParameterId);
        HashSet<long> filterableCategoryIdSet = ParameterFilterUtilities.GetAllFilterableCategories()
            .Select(RevitElementIds.GetValue)
            .ToHashSet();

        return selectedCategoryIds
            .Where(categoryId =>
            {
                long categoryIdValue = RevitElementIds.GetValue(categoryId);
                if (!applicableCategoryIdSet.Contains(categoryIdValue)
                    || !filterableCategoryIdSet.Contains(categoryIdValue))
                {
                    return false;
                }

                ICollection<ElementId> filterableParameters = ParameterFilterUtilities.GetFilterableParametersInCommon(
                    document,
                    new List<ElementId> { categoryId });
                return filterableParameters.Any(id => RevitElementIds.GetValue(id) == parameterId);
            })
            .ToList();
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
#if REVIT2023_OR_GREATER
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
            .FirstOrDefault(filter => string.Equals(filter.Name, filterName, StringComparison.OrdinalIgnoreCase));
    }

    private static OverrideGraphicSettings CreateOverrides(ColorRuleRow row, FillPatternElement? solidFillPattern,
        bool colorPatterns, bool colorLines)
    {
        RevitColor color = new(row.Red, row.Green, row.Blue);
        OverrideGraphicSettings settings = new();
        if (colorPatterns)
        {
            settings.SetSurfaceForegroundPatternColor(color);
            settings.SetSurfaceForegroundPatternVisible(true);
            settings.SetCutForegroundPatternColor(color);
            settings.SetCutForegroundPatternVisible(true);
            if (solidFillPattern is not null)
            {
                settings.SetSurfaceForegroundPatternId(solidFillPattern.Id);
                settings.SetCutForegroundPatternId(solidFillPattern.Id);
            }
        }
        if (colorLines)
        {
            settings.SetProjectionLineColor(color);
            settings.SetCutLineColor(color);
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
