using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Modules.SharedParameters.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public sealed class SharedParameterProjectAnalysisService
{
    private readonly SharedParameterProjectCatalogService catalogService;
    private readonly SharedParameterViewFilterService viewFilterService;
    private readonly SharedParameterUsageSummaryBuilder summaryBuilder;
    private readonly ITrueBimLogger logger;

    public SharedParameterProjectAnalysisService(
        SharedParameterProjectCatalogService catalogService,
        SharedParameterViewFilterService viewFilterService,
        SharedParameterUsageSummaryBuilder summaryBuilder,
        ITrueBimLogger logger)
    {
        this.catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        this.viewFilterService = viewFilterService ?? throw new ArgumentNullException(nameof(viewFilterService));
        this.summaryBuilder = summaryBuilder ?? throw new ArgumentNullException(nameof(summaryBuilder));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SharedParameterProjectAnalysis AnalyzeQuick(
        Document document,
        SharedParameterDescriptor parameter,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(parameter, nameof(parameter));

        List<AnalysisError> errors = [];
        IReadOnlyList<ElementParameterUsage> elements = RunPhase(
            "Элементы",
            () => CollectElementUsages(document, parameter, cancellationToken),
            errors);
        IReadOnlyList<ScheduleFieldUsage> schedules = RunPhase(
            "Спецификации",
            () => CollectScheduleUsages(document, parameter, cancellationToken),
            errors);
        IReadOnlyList<ViewFilterUsage> filters = RunPhase(
            "Фильтры видов",
            () => CollectViewFilterUsages(document, parameter, cancellationToken),
            errors);
        IReadOnlyList<GlobalParameterAssociationUsage> globalParameters = RunPhase(
            "Глобальные параметры",
            () => CollectGlobalAssociations(document, parameter, elements, cancellationToken),
            errors);
        List<DeletionBlocker> blockers = CollectWorksharingBlockers(
                document,
                parameter,
                schedules,
                filters)
            .ToList();
        blockers.AddRange(errors.Select(error => new DeletionBlocker(
            "PROJECT_ANALYSIS_INCOMPLETE",
            $"Фаза «{error.Phase}» завершилась ошибкой: {error.Message}",
            "Document",
            null,
            DetectionConfidence.ManualCheckRequired)));

        logger.Info(
            $"Shared Parameter Inspector quick analysis completed. "
            + $"Guid={parameter.Guid:D}; Elements={elements.Count}; Schedules={schedules.Count}; "
            + $"ViewFilters={filters.Count}; GlobalAssociations={globalParameters.Count}; Errors={errors.Count}.");

        return new SharedParameterProjectAnalysis(
            catalogService.GetDocumentIdentity(document),
            parameter,
            DateTimeOffset.Now,
            elements,
            summaryBuilder.BuildElementAggregates(elements),
            schedules,
            filters,
            globalParameters,
            [],
            blockers,
            [],
            errors);
    }

    public SharedParameterProjectAnalysis WithFamilyPresence(
        Document document,
        SharedParameterProjectAnalysis analysis,
        IReadOnlyList<ProjectFamilyPresence> families)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(analysis, nameof(analysis));
        families ??= [];

        List<DeletionBlocker> blockers = analysis.Blockers.ToList();
        foreach (ProjectFamilyPresence family in families.Where(family =>
                     family.Status is FamilyPresenceStatus.Unsupported
                         or FamilyPresenceStatus.CannotOpen
                         or FamilyPresenceStatus.Failed))
        {
            blockers.Add(new DeletionBlocker(
                "FAMILY_NOT_VERIFIED",
                $"Семейство «{family.FamilyName}» не удалось проверить: {family.ErrorMessage ?? family.Status.ToString()}.",
                "Family",
                family.FamilyId,
                DetectionConfidence.ManualCheckRequired));
        }

        if (document.IsWorkshared)
        {
            blockers.AddRange(families
                .Where(family => family.ContainsParameter)
                .Select(family => CreateWorksharingBlocker(
                    document,
                    family.FamilyId,
                    "Family"))
                .Where(blocker => blocker is not null)
                .Cast<DeletionBlocker>());
        }

        return analysis with
        {
            Families = families,
            Blockers = blockers
                .GroupBy(blocker => $"{blocker.Code}|{blocker.ElementId}|{blocker.Message}", StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList()
        };
    }

    public IReadOnlyList<ElementParameterUsage> CollectElementUsages(
        Document document,
        SharedParameterDescriptor parameter,
        CancellationToken cancellationToken = default)
    {
        List<ElementParameterUsage> usages = [];
        foreach (CategoryDescriptor category in parameter.Categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FilteredElementCollector collector = new(document);
            collector.OfCategoryId(RevitElementIds.Create(category.ElementId));
            if (parameter.BindingKind == SharedParameterBindingKind.Type)
            {
                collector.WhereElementIsElementType();
            }
            else
            {
                collector.WhereElementIsNotElementType();
            }

            foreach (Element element in collector)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (element is RevitLinkInstance or RevitLinkType)
                {
                    continue;
                }

                Parameter? value = element.get_Parameter(parameter.Guid);
                usages.Add(CreateElementUsage(element, category.Name, value));
            }
        }

        return summaryBuilder.DeduplicateElements(usages);
    }

    public IReadOnlyList<ScheduleFieldUsage> CollectScheduleUsages(
        Document document,
        SharedParameterDescriptor parameter,
        CancellationToken cancellationToken = default)
    {
        List<ScheduleFieldUsage> usages = [];
        foreach (ViewSchedule schedule in new FilteredElementCollector(document)
                     .OfClass(typeof(ViewSchedule))
                     .Cast<ViewSchedule>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            CollectScheduleDefinitionUsages(
                schedule,
                schedule.Definition,
                parameter.ParameterElementId,
                isEmbedded: false,
                usages);
            if (schedule.Definition.HasEmbeddedSchedule)
            {
                CollectScheduleDefinitionUsages(
                    schedule,
                    schedule.Definition.EmbeddedDefinition,
                    parameter.ParameterElementId,
                    isEmbedded: true,
                    usages);
            }
        }

        return usages;
    }

    public IReadOnlyList<ViewFilterUsage> CollectViewFilterUsages(
        Document document,
        SharedParameterDescriptor parameter,
        CancellationToken cancellationToken = default)
    {
        Dictionary<long, IReadOnlyList<AppliedViewFilterUsage>> applications = CollectFilterApplications(document);
        List<ViewFilterUsage> usages = [];
        foreach (ParameterFilterElement filter in new FilteredElementCollector(document)
                     .OfClass(typeof(ParameterFilterElement))
                     .Cast<ParameterFilterElement>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ViewFilterUsage? usage = viewFilterService.Analyze(
                document,
                filter,
                parameter.ParameterElementId,
                applications);
            if (usage is not null)
            {
                usages.Add(usage);
            }
        }

        return usages;
    }

    public IReadOnlyList<GlobalParameterAssociationUsage> CollectGlobalAssociations(
        Document document,
        SharedParameterDescriptor parameter,
        IReadOnlyList<ElementParameterUsage> elementUsages,
        CancellationToken cancellationToken = default)
    {
        List<GlobalParameterAssociationUsage> associations = [];
        foreach (ElementParameterUsage usage in elementUsages.Where(usage => usage.HasParameter))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Element? element = document.GetElement(RevitElementIds.Create(usage.ElementId));
            Parameter? value = element?.get_Parameter(parameter.Guid);
            if (element is null || value is null)
            {
                continue;
            }

            try
            {
                ElementId globalParameterId = value.GetAssociatedGlobalParameter();
                if (globalParameterId == ElementId.InvalidElementId
                    || document.GetElement(globalParameterId) is not GlobalParameter globalParameter)
                {
                    continue;
                }

                associations.Add(new GlobalParameterAssociationUsage(
                    usage.ElementId,
                    usage.Name,
                    usage.CategoryName,
                    RevitElementIds.GetValue(globalParameterId),
                    globalParameter.Name,
                    globalParameter.IsDrivenByFormula ? globalParameter.GetFormula() : string.Empty,
                    false,
                    1));
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // The parameter cannot be associated with a global parameter in this context.
            }
        }

        return associations
            .GroupBy(association => (association.ElementId, association.GlobalParameterId))
            .Select(group => group.First())
            .ToList();
    }

    private static ElementParameterUsage CreateElementUsage(
        Element element,
        string fallbackCategoryName,
        Parameter? parameter)
    {
        string familyName = string.Empty;
        string typeName = string.Empty;
        if (element is FamilyInstance instance)
        {
            familyName = instance.Symbol?.FamilyName ?? string.Empty;
            typeName = instance.Symbol?.Name ?? string.Empty;
        }
        else if (element is ElementType elementType)
        {
            familyName = elementType.FamilyName ?? string.Empty;
            typeName = elementType.Name ?? string.Empty;
        }
        else
        {
            ElementId typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId && element.Document.GetElement(typeId) is ElementType type)
            {
                familyName = type.FamilyName ?? string.Empty;
                typeName = type.Name ?? string.Empty;
            }
        }

        return new ElementParameterUsage(
            RevitElementIds.GetValue(element.Id),
            element.UniqueId ?? string.Empty,
            element.Name ?? string.Empty,
            element.Category?.Name ?? fallbackCategoryName,
            familyName,
            typeName,
            element is ElementType,
            parameter is not null,
            parameter?.HasValue == true,
            parameter?.IsReadOnly == true,
            parameter is null ? null : GetDisplayValue(parameter));
    }

    private static string GetDisplayValue(Parameter parameter)
    {
        try
        {
            string? value = parameter.AsValueString();
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            return parameter.StorageType switch
            {
                StorageType.String => parameter.AsString() ?? string.Empty,
                StorageType.Integer => parameter.AsInteger().ToString(),
                StorageType.Double => parameter.AsDouble().ToString("R"),
                StorageType.ElementId => RevitElementIds.GetValue(parameter.AsElementId()).ToString(),
                _ => string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void CollectScheduleDefinitionUsages(
        ViewSchedule schedule,
        ScheduleDefinition definition,
        long targetParameterId,
        bool isEmbedded,
        ICollection<ScheduleFieldUsage> output)
    {
        IList<ScheduleFieldId> fieldOrder = definition.GetFieldOrder();
        bool hasOpaqueDependencies = fieldOrder
            .Select(definition.GetField)
            .Any(field => field.IsCalculatedField || field.IsCombinedParameterField);

        for (int index = 0; index < fieldOrder.Count; index++)
        {
            ScheduleFieldId fieldId = fieldOrder[index];
            ScheduleField field = definition.GetField(fieldId);
            if (RevitElementIds.GetValue(field.ParameterId) != targetParameterId)
            {
                continue;
            }

            bool usedInFilter = definition.GetFilters().Any(filter => filter.FieldId == fieldId);
            bool usedInSort = definition.GetSortGroupFields().Any(sort => sort.FieldId == fieldId);
            output.Add(new ScheduleFieldUsage(
                RevitElementIds.GetValue(schedule.Id),
                schedule.Name,
                fieldId.IntegerValue,
                field.GetName(),
                field.ColumnHeading,
                index,
                field.IsHidden,
                usedInFilter,
                usedInSort,
                isEmbedded,
                hasOpaqueDependencies,
                hasOpaqueDependencies ? DetectionConfidence.Probable : DetectionConfidence.Exact));
        }
    }

    private static Dictionary<long, IReadOnlyList<AppliedViewFilterUsage>> CollectFilterApplications(
        Document document)
    {
        HashSet<long> placedViewIds = new FilteredElementCollector(document)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Select(viewport => RevitElementIds.GetValue(viewport.ViewId))
            .ToHashSet();
        Dictionary<long, List<AppliedViewFilterUsage>> applications = [];

        foreach (View view in new FilteredElementCollector(document)
                     .OfClass(typeof(View))
                     .Cast<View>()
                     .Where(view => view.ViewType != ViewType.Internal))
        {
            ICollection<ElementId> filterIds;
            try
            {
                filterIds = view.GetFilters();
            }
            catch
            {
                continue;
            }

            foreach (ElementId filterId in filterIds)
            {
                long id = RevitElementIds.GetValue(filterId);
                if (!applications.TryGetValue(id, out List<AppliedViewFilterUsage>? rows))
                {
                    rows = [];
                    applications[id] = rows;
                }

                bool isVisible = SafeGetFilterVisibility(view, filterId);
                bool hasOverrides = SafeHasOverrides(view, filterId);
                rows.Add(new AppliedViewFilterUsage(
                    RevitElementIds.GetValue(view.Id),
                    view.Name,
                    view.ViewType.ToString(),
                    view.IsTemplate,
                    isVisible,
                    hasOverrides,
                    placedViewIds.Contains(RevitElementIds.GetValue(view.Id))));
            }
        }

        return applications.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<AppliedViewFilterUsage>)pair.Value);
    }

    private static bool SafeGetFilterVisibility(View view, ElementId filterId)
    {
        try
        {
            return view.GetFilterVisibility(filterId);
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeHasOverrides(View view, ElementId filterId)
    {
        try
        {
            OverrideGraphicSettings settings = view.GetFilterOverrides(filterId);
            return settings.ProjectionLineColor.IsValid
                || settings.ProjectionLineWeight >= 0
                || settings.SurfaceForegroundPatternId != ElementId.InvalidElementId
                || settings.Transparency > 0
                || settings.Halftone;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<DeletionBlocker> CollectWorksharingBlockers(
        Document document,
        SharedParameterDescriptor parameter,
        IReadOnlyList<ScheduleFieldUsage> schedules,
        IReadOnlyList<ViewFilterUsage> filters)
    {
        if (!document.IsWorkshared)
        {
            return [];
        }

        List<(long ElementId, string ObjectKind)> targets =
        [
            (parameter.ParameterElementId, "SharedParameterElement")
        ];
        targets.AddRange(schedules.Select(schedule => (schedule.ScheduleId, "ViewSchedule")));
        targets.AddRange(filters.Select(filter => (filter.FilterId, "ParameterFilterElement")));

        List<DeletionBlocker> blockers = [];
        foreach ((long elementId, string objectKind) in targets.Distinct())
        {
            DeletionBlocker? blocker = CreateWorksharingBlocker(document, elementId, objectKind);
            if (blocker is null)
            {
                continue;
            }

            blockers.Add(blocker);
        }

        return blockers;
    }

    private static DeletionBlocker? CreateWorksharingBlocker(
        Document document,
        long elementId,
        string objectKind)
    {
        ElementId id = RevitElementIds.Create(elementId);
        CheckoutStatus status = WorksharingUtils.GetCheckoutStatus(document, id);
        if (status.ToString().IndexOf("Other", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        string owner = string.Empty;
        try
        {
            owner = WorksharingUtils.GetWorksharingTooltipInfo(document, id).Owner;
        }
        catch
        {
            // Ownership still blocks the action even if the owner cannot be resolved.
        }

        return new DeletionBlocker(
            "WORKSHARING_OWNED_BY_OTHER",
            string.IsNullOrWhiteSpace(owner)
                ? $"{objectKind} {elementId} недоступен для редактирования."
                : $"{objectKind} {elementId} принадлежит пользователю {owner}.",
            objectKind,
            elementId,
            DetectionConfidence.Exact);
    }

    private static IReadOnlyList<T> RunPhase<T>(
        string phase,
        Func<IReadOnlyList<T>> action,
        ICollection<AnalysisError> errors)
    {
        try
        {
            return action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            errors.Add(new AnalysisError(phase, exception.Message, null));
            return [];
        }
    }
}
