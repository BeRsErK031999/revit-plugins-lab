using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Modules.SharedParameters.Services;

public sealed class SharedParameterDeletionPlanBuilder
{
    public SharedParameterDeletionPlan Build(
        SharedParameterProjectAnalysis analysis,
        SharedParameterDryRunResult dryRun,
        IReadOnlyList<FamilyParameterUsageReport> familyReports)
    {
        if (analysis is null)
        {
            throw new ArgumentNullException(nameof(analysis));
        }

        if (dryRun is null)
        {
            throw new ArgumentNullException(nameof(dryRun));
        }

        familyReports ??= [];

        List<DeletionBlocker> blockers = [];
        blockers.AddRange(analysis.Blockers);
        blockers.AddRange(dryRun.Blockers);
        blockers.AddRange(familyReports.SelectMany(report => report.DeletionBlockers));

        List<DeletionWarning> warnings = [];
        warnings.AddRange(analysis.Warnings);
        if (analysis.Document.IsWorkshared)
        {
            warnings.Add(new DeletionWarning(
                "WORKSHARING_RECOMMENDATION",
                "Перед удалением рекомендуется сохранить проект и выполнить синхронизацию с центральной моделью.",
                "Document",
                null));
        }

        if (!dryRun.ParameterRestoredAfterRollback)
        {
            blockers.Add(new DeletionBlocker(
                "DRY_RUN_ROLLBACK_NOT_CONFIRMED",
                "После пробного удаления не подтверждено восстановление SharedParameterElement.",
                "SharedParameterElement",
                analysis.Parameter.ParameterElementId,
                DetectionConfidence.Exact));
        }

        foreach (DryRunDeletedElement item in dryRun.DeletedElements.Where(item => !item.WasDiscoveredByAnalysis))
        {
            blockers.Add(new DeletionBlocker(
                "UNKNOWN_CASCADE_DEPENDENCY",
                $"Dry run обнаружил неизвестную каскадную зависимость: {item.ElementType} «{item.ElementName}» (ElementId {item.ElementId}).",
                item.ElementType,
                item.ElementId,
                DetectionConfidence.Exact));
        }

        IReadOnlyList<ScheduleDeletionAction> scheduleActions = BuildScheduleActions(analysis.ScheduleFields, blockers);
        IReadOnlyList<ViewFilterDeletionAction> filterActions = BuildViewFilterActions(analysis.ViewFilters, blockers);
        IReadOnlyList<GlobalParameterDeletionAction> globalActions = BuildGlobalActions(analysis.GlobalParameterAssociations);
        IReadOnlyList<FamilyDeletionAction> familyActions = BuildFamilyActions(
            analysis.Families,
            familyReports,
            blockers);

        IReadOnlyList<DeletionBlocker> uniqueBlockers = blockers
            .GroupBy(blocker => $"{blocker.Code}|{blocker.ObjectKind}|{blocker.ElementId}|{blocker.Message}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        bool allActionsSupported = scheduleActions.All(action => action.Support == DeletionActionSupport.Supported)
            && filterActions.All(action => action.Support == DeletionActionSupport.Supported)
            && globalActions.All(action => action.Support == DeletionActionSupport.Supported)
            && familyActions.All(action => action.Support == DeletionActionSupport.Supported);

        return new SharedParameterDeletionPlan(
            analysis.Parameter,
            scheduleActions,
            filterActions,
            globalActions,
            familyActions,
            dryRun.DeletedElements.Select(item => item.ElementId).Distinct().ToList(),
            uniqueBlockers,
            warnings
                .GroupBy(warning => $"{warning.Code}|{warning.ObjectKind}|{warning.ElementId}|{warning.Message}", StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList(),
            uniqueBlockers.Count == 0 && allActionsSupported);
    }

    private static IReadOnlyList<ScheduleDeletionAction> BuildScheduleActions(
        IReadOnlyList<ScheduleFieldUsage> fields,
        ICollection<DeletionBlocker> blockers)
    {
        List<ScheduleDeletionAction> actions = [];
        foreach (ScheduleFieldUsage field in fields)
        {
            if (field.HasCalculatedOrCombinedDependencies)
            {
                blockers.Add(new DeletionBlocker(
                    "SCHEDULE_DEPENDENCY_UNRESOLVED",
                    $"Спецификация «{field.ScheduleName}» содержит расчётные или объединённые поля, зависимость которых от удаляемого поля нельзя подтвердить публичным API.",
                    "ViewSchedule",
                    field.ScheduleId,
                    DetectionConfidence.Probable));
                actions.Add(new ScheduleDeletionAction(
                    field.ScheduleId,
                    field.ScheduleName,
                    field.FieldId,
                    "Заблокировать",
                    "Есть неподтверждённые расчётные или объединённые зависимости.",
                    DeletionRisk.Blocking,
                    true,
                    DeletionActionSupport.Unsupported));
                continue;
            }

            actions.Add(new ScheduleDeletionAction(
                field.ScheduleId,
                field.ScheduleName,
                field.FieldId,
                "Удалить зависимости поля и поле",
                "Поле напрямую ссылается на SharedParameterElement.",
                field.UsedInFilter || field.UsedInSortOrGroup ? DeletionRisk.Medium : DeletionRisk.Low,
                true,
                DeletionActionSupport.Supported));
        }

        return actions;
    }

    private static IReadOnlyList<ViewFilterDeletionAction> BuildViewFilterActions(
        IReadOnlyList<ViewFilterUsage> filters,
        ICollection<DeletionBlocker> blockers)
    {
        List<ViewFilterDeletionAction> actions = [];
        foreach (ViewFilterUsage filter in filters)
        {
            if (filter.OtherRules.Count == 0)
            {
                actions.Add(new ViewFilterDeletionAction(
                    filter.FilterId,
                    filter.FilterName,
                    "Снять с видов и удалить фильтр",
                    "Фильтр состоит только из правил удаляемого параметра.",
                    DeletionRisk.Medium,
                    true,
                    DeletionActionSupport.Supported));
                continue;
            }

            if (filter.CanRebuildWithoutTarget)
            {
                actions.Add(new ViewFilterDeletionAction(
                    filter.FilterId,
                    filter.FilterName,
                    "Перестроить дерево правил",
                    "Другие правила и применение фильтра будут сохранены.",
                    DeletionRisk.Medium,
                    true,
                    DeletionActionSupport.Supported));
                continue;
            }

            blockers.Add(new DeletionBlocker(
                "VIEW_FILTER_TREE_UNSUPPORTED",
                $"Дерево фильтра «{filter.FilterName}» нельзя безопасно перестроить без правил выбранного параметра.",
                "ParameterFilterElement",
                filter.FilterId,
                filter.Confidence));
            actions.Add(new ViewFilterDeletionAction(
                filter.FilterId,
                filter.FilterName,
                "Заблокировать",
                "Дерево правил не поддерживает безопасное частичное перестроение.",
                DeletionRisk.Blocking,
                true,
                DeletionActionSupport.Unsupported));
        }

        return actions;
    }

    private static IReadOnlyList<GlobalParameterDeletionAction> BuildGlobalActions(
        IReadOnlyList<GlobalParameterAssociationUsage> associations)
    {
        return associations
            .Select(association => new GlobalParameterDeletionAction(
                association.ElementId,
                association.GlobalParameterId,
                association.GlobalParameterName,
                "Снять ассоциацию",
                "Глобальный параметр сохраняется; снимается только ассоциация элемента.",
                DeletionRisk.Low,
                true,
                DeletionActionSupport.Supported))
            .ToList();
    }

    private static IReadOnlyList<FamilyDeletionAction> BuildFamilyActions(
        IReadOnlyList<ProjectFamilyPresence> families,
        IReadOnlyList<FamilyParameterUsageReport> familyReports,
        ICollection<DeletionBlocker> blockers)
    {
        Dictionary<long, FamilyParameterUsageReport> reportsByFamilyId = familyReports
            .Where(report => report.Family.ProjectFamilyId.HasValue)
            .GroupBy(report => report.Family.ProjectFamilyId!.Value)
            .ToDictionary(group => group.Key, group => group.First());
        List<FamilyDeletionAction> actions = [];

        foreach (ProjectFamilyPresence family in families.Where(family => family.ContainsParameter))
        {
            if (!reportsByFamilyId.TryGetValue(family.FamilyId, out FamilyParameterUsageReport? report))
            {
                blockers.Add(new DeletionBlocker(
                    "FAMILY_DEEP_ANALYSIS_REQUIRED",
                    $"Перед удалением требуется глубокий анализ семейства «{family.FamilyName}».",
                    "Family",
                    family.FamilyId,
                    DetectionConfidence.Exact));
                actions.Add(new FamilyDeletionAction(
                    family.FamilyId,
                    family.FamilyName,
                    "Заблокировать",
                    "Нет актуального глубокого анализа.",
                    DeletionRisk.Blocking,
                    true,
                    DeletionActionSupport.Unsupported));
                continue;
            }

            if (report.DeletionBlockers.Count > 0 || report.Errors.Count > 0)
            {
                actions.Add(new FamilyDeletionAction(
                    family.FamilyId,
                    family.FamilyName,
                    "Заблокировать",
                    "В семействе найдены формулы, размеры, ассоциации, annotation-ограничения или ошибки.",
                    DeletionRisk.Blocking,
                    true,
                    DeletionActionSupport.Unsupported));
                continue;
            }

            actions.Add(new FamilyDeletionAction(
                family.FamilyId,
                family.FamilyName,
                "Удалить параметр и загрузить семейство обратно",
                "Глубокий анализ не обнаружил зависимостей.",
                DeletionRisk.High,
                true,
                DeletionActionSupport.Supported));
        }

        foreach (ProjectFamilyPresence family in families.Where(family =>
                     family.Status is FamilyPresenceStatus.Unsupported
                         or FamilyPresenceStatus.CannotOpen
                         or FamilyPresenceStatus.Failed))
        {
            blockers.Add(new DeletionBlocker(
                "FAMILY_NOT_VERIFIED",
                $"Семейство «{family.FamilyName}» не удалось подтвердить: {family.ErrorMessage ?? family.Status.ToString()}.",
                "Family",
                family.FamilyId,
                DetectionConfidence.ManualCheckRequired));
        }

        return actions;
    }
}
