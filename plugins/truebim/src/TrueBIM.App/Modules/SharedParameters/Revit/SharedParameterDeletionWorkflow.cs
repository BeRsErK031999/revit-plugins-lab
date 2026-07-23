using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SharedParameters.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.SharedParameters.Revit;

public sealed class SharedParameterDeletionWorkflow
{
    private readonly SharedParameterProjectCatalogService catalogService;
    private readonly SharedParameterProjectAnalysisService analysisService;
    private readonly SharedParameterFamilyAnalysisService familyAnalysisService;
    private readonly SharedParameterViewFilterService viewFilterService;
    private readonly ISharedParameterVersionAdapter versionAdapter;
    private readonly ITrueBimLogger logger;

    public SharedParameterDeletionWorkflow(
        SharedParameterProjectCatalogService catalogService,
        SharedParameterProjectAnalysisService analysisService,
        SharedParameterFamilyAnalysisService familyAnalysisService,
        SharedParameterViewFilterService viewFilterService,
        ISharedParameterVersionAdapter versionAdapter,
        ITrueBimLogger logger)
    {
        this.catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        this.analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        this.familyAnalysisService = familyAnalysisService ?? throw new ArgumentNullException(nameof(familyAnalysisService));
        this.viewFilterService = viewFilterService ?? throw new ArgumentNullException(nameof(viewFilterService));
        this.versionAdapter = versionAdapter ?? throw new ArgumentNullException(nameof(versionAdapter));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SharedParameterDryRunResult DryRun(
        Document document,
        SharedParameterProjectAnalysis analysis)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(analysis, nameof(analysis));

        Transaction? transaction = null;
        ICollection<ElementId> deletedIds = [];
        List<AnalysisError> errors = [];
        try
        {
            SharedParameterElement? parameter = SharedParameterElement.Lookup(document, analysis.Parameter.Guid);
            if (parameter is null)
            {
                return new SharedParameterDryRunResult(
                    [],
                    false,
                    [new DeletionBlocker(
                        "PARAMETER_NOT_FOUND",
                        "SharedParameterElement больше не существует в активном проекте.",
                        "SharedParameterElement",
                        analysis.Parameter.ParameterElementId,
                        DetectionConfidence.Exact)],
                    []);
            }

            transaction = new Transaction(document, $"TrueBIM: dry run {analysis.Parameter.Name}");
            transaction.Start();
            deletedIds = document.Delete(parameter.Id);
            transaction.RollBack();
            transaction = null;

            bool restored = SharedParameterElement.Lookup(document, analysis.Parameter.Guid) is not null;
            HashSet<long> discoveredIds = BuildDiscoveredElementIdSet(analysis);
            IReadOnlyList<DryRunDeletedElement> deletedElements = deletedIds
                .Select(id => CreateDryRunElement(document, id, discoveredIds))
                .ToList();
            logger.Info(
                $"Shared Parameter Inspector dry run rolled back. "
                + $"Guid={analysis.Parameter.Guid:D}; DeletedIds={deletedElements.Count}; Restored={restored}.");
            return new SharedParameterDryRunResult(deletedElements, restored, [], errors);
        }
        catch (Exception exception)
        {
            if (transaction?.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            errors.Add(new AnalysisError("Dry run", exception.Message, null));
            logger.Error(
                $"Shared Parameter Inspector dry run failed for {analysis.Parameter.Guid:D}.",
                exception);
            return new SharedParameterDryRunResult(
                [],
                SharedParameterElement.Lookup(document, analysis.Parameter.Guid) is not null,
                [new DeletionBlocker(
                    "DRY_RUN_FAILED",
                    $"Пробное удаление не выполнено: {exception.Message}",
                    "SharedParameterElement",
                    analysis.Parameter.ParameterElementId,
                    DetectionConfidence.Exact)],
                errors);
        }
    }

    public SharedParameterDeletionResult Execute(
        Document document,
        SharedParameterProjectAnalysis analysis,
        SharedParameterDeletionPlan plan,
        IReadOnlyList<FamilyParameterUsageReport> familyReports,
        DeletionMode mode,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(analysis, nameof(analysis));
        Guard.NotNull(plan, nameof(plan));
        familyReports ??= [];

        string userName = document.Application.Username ?? Environment.UserName;
        logger.Info(
            $"Shared Parameter Inspector deletion requested. "
            + $"Document='{document.Title}'; Guid={analysis.Parameter.Guid:D}; "
            + $"ElementId={analysis.Parameter.ParameterElementId}; User='{userName}'; Mode={mode}; "
            + $"Schedules={plan.Schedules.Count}; ViewFilters={plan.ViewFilters.Count}; "
            + $"GlobalAssociations={plan.GlobalParameters.Count}; Families={plan.Families.Count}; "
            + $"Blockers={plan.Blockers.Count}; Warnings={plan.Warnings.Count}.");
        if (plan.Blockers.Count > 0 || (mode == DeletionMode.Safe && !plan.CanExecuteSafely))
        {
            logger.Warning(
                $"Shared Parameter Inspector deletion blocked. "
                + $"Guid={analysis.Parameter.Guid:D}; Mode={mode}; Blockers={plan.Blockers.Count}.");
            return CreateResult(
                document,
                analysis,
                plan,
                userName,
                mode,
                DeletionStatus.Blocked,
                [],
                [],
                [],
                plan.Families.Select(family => family.FamilyName).ToList(),
                [],
                "Удаление заблокировано: устраните все blockers и повторите свежий анализ.");
        }

        SharedParameterDescriptor? freshParameter = catalogService.Find(document, analysis.Parameter.Guid);
        if (freshParameter is null
            || freshParameter.ParameterElementId != analysis.Parameter.ParameterElementId)
        {
            return CreateResult(
                document,
                analysis,
                plan,
                userName,
                mode,
                DeletionStatus.Blocked,
                [],
                [],
                [],
                [],
                [new AnalysisError(
                    "Предварительная проверка",
                    "Параметр изменился после анализа. Выполните анализ повторно.",
                    null)],
                "Удаление не запущено: кэш анализа устарел.");
        }

        TransactionGroup? group = null;
        List<long> changedIds = [];
        List<long> deletedIds = [];
        List<string> processedFamilies = [];
        List<string> skippedFamilies = [];
        List<AnalysisError> errors = [];

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            group = new TransactionGroup(document, $"Удаление общего параметра: {analysis.Parameter.Name}");
            group.Start();

            ExecuteScheduleActions(document, analysis, plan.Schedules, changedIds, cancellationToken);
            ExecuteViewFilterActions(document, analysis, plan.ViewFilters, changedIds, deletedIds, cancellationToken);
            ExecuteGlobalParameterActions(document, analysis, plan.GlobalParameters, changedIds, cancellationToken);
            ExecuteFamilyActions(
                document,
                analysis,
                plan.Families,
                familyReports,
                changedIds,
                processedFamilies,
                skippedFamilies,
                cancellationToken);
            ExecuteParameterDeletion(document, analysis.Parameter, changedIds, deletedIds, cancellationToken);

            VerifyResult(
                document,
                analysis,
                plan,
                processedFamilies,
                deletedIds,
                cancellationToken);
            group.Assimilate();
            group = null;

            logger.Info(
                $"Shared Parameter Inspector deletion completed. "
                + $"Guid={analysis.Parameter.Guid:D}; Mode={mode}; Deleted={deletedIds.Count}; "
                + $"Changed={changedIds.Count}; Families={processedFamilies.Count}.");
            return CreateResult(
                document,
                analysis,
                plan,
                userName,
                mode,
                DeletionStatus.Success,
                changedIds,
                deletedIds,
                processedFamilies,
                skippedFamilies,
                errors,
                "Параметр и все подтверждённые поддерживаемые зависимости удалены. Контрольный анализ не обнаружил остаточных ссылок.");
        }
        catch (OperationCanceledException)
        {
            if (group?.GetStatus() == TransactionStatus.Started)
            {
                group.RollBack();
            }

            logger.Warning(
                $"Shared Parameter Inspector deletion cancelled and rolled back. "
                + $"Guid={analysis.Parameter.Guid:D}; Mode={mode}; User='{userName}'.");
            return CreateResult(
                document,
                analysis,
                plan,
                userName,
                mode,
                DeletionStatus.Cancelled,
                changedIds,
                deletedIds,
                processedFamilies,
                skippedFamilies,
                errors,
                "Операция отменена. Проектная TransactionGroup откатилась.");
        }
        catch (Exception exception)
        {
            if (group?.GetStatus() == TransactionStatus.Started)
            {
                group.RollBack();
            }

            errors.Add(new AnalysisError("Удаление", exception.Message, null));
            logger.Error(
                $"Shared Parameter Inspector deletion rolled back for {analysis.Parameter.Guid:D}.",
                exception);
            return CreateResult(
                document,
                analysis,
                plan,
                userName,
                mode,
                DeletionStatus.RolledBack,
                changedIds,
                deletedIds,
                processedFamilies,
                skippedFamilies,
                errors,
                "Удаление не завершено. Проектная TransactionGroup откатилась; подробности записаны в отчёт.");
        }
    }

    private static void ExecuteScheduleActions(
        Document document,
        SharedParameterProjectAnalysis analysis,
        IReadOnlyList<ScheduleDeletionAction> actions,
        ICollection<long> changedIds,
        CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            return;
        }

        using Transaction transaction = new(document, "TrueBIM: удалить поля общего параметра");
        transaction.Start();
        foreach (ScheduleDeletionAction action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.GetElement(RevitElementIds.Create(action.ScheduleId)) is not ViewSchedule schedule)
            {
                throw new InvalidOperationException($"Спецификация {action.ScheduleId} больше не существует.");
            }

            ScheduleFieldUsage usage = analysis.ScheduleFields.First(field =>
                field.ScheduleId == action.ScheduleId && field.FieldId == action.FieldId);
            ScheduleDefinition definition = usage.IsEmbeddedDefinition
                ? schedule.Definition.EmbeddedDefinition
                : schedule.Definition;
            ScheduleFieldId fieldId = new(action.FieldId);

            for (int index = definition.GetFilterCount() - 1; index >= 0; index--)
            {
                if (definition.GetFilter(index).FieldId == fieldId)
                {
                    definition.RemoveFilter(index);
                }
            }

            for (int index = definition.GetSortGroupFieldCount() - 1; index >= 0; index--)
            {
                if (definition.GetSortGroupField(index).FieldId == fieldId)
                {
                    definition.RemoveSortGroupField(index);
                }
            }

            definition.RemoveField(fieldId);
            changedIds.Add(action.ScheduleId);
        }

        transaction.Commit();
    }

    private void ExecuteViewFilterActions(
        Document document,
        SharedParameterProjectAnalysis analysis,
        IReadOnlyList<ViewFilterDeletionAction> actions,
        ICollection<long> changedIds,
        ICollection<long> deletedIds,
        CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            return;
        }

        using Transaction transaction = new(document, "TrueBIM: обработать фильтры общего параметра");
        transaction.Start();
        foreach (ViewFilterDeletionAction action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ElementId filterId = RevitElementIds.Create(action.FilterId);
            if (document.GetElement(filterId) is not ParameterFilterElement filter)
            {
                throw new InvalidOperationException($"Фильтр {action.FilterId} больше не существует.");
            }

            ViewFilterUsage usage = analysis.ViewFilters.First(item => item.FilterId == action.FilterId);
            if (usage.OtherRules.Count == 0)
            {
                foreach (AppliedViewFilterUsage application in usage.AppliedViews)
                {
                    if (document.GetElement(RevitElementIds.Create(application.ViewId)) is View view
                        && view.GetFilters().Contains(filterId))
                    {
                        view.RemoveFilter(filterId);
                        changedIds.Add(application.ViewId);
                    }
                }

                foreach (ElementId deletedId in document.Delete(filterId))
                {
                    deletedIds.Add(RevitElementIds.GetValue(deletedId));
                }

                continue;
            }

            ElementFilter source = filter.GetElementFilter();
            if (!viewFilterService.TryBuildFilterWithoutTarget(
                    source,
                    analysis.Parameter.ParameterElementId,
                    out ElementFilter? rebuilt)
                || rebuilt is null
                || !filter.SetElementFilter(rebuilt))
            {
                throw new InvalidOperationException(
                    $"Не удалось безопасно перестроить фильтр «{filter.Name}».");
            }

            changedIds.Add(action.FilterId);
        }

        transaction.Commit();
    }

    private static void ExecuteGlobalParameterActions(
        Document document,
        SharedParameterProjectAnalysis analysis,
        IReadOnlyList<GlobalParameterDeletionAction> actions,
        ICollection<long> changedIds,
        CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            return;
        }

        using Transaction transaction = new(document, "TrueBIM: снять ассоциации глобальных параметров");
        transaction.Start();
        foreach (GlobalParameterDeletionAction action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Element? element = document.GetElement(RevitElementIds.Create(action.ElementId));
            Parameter? parameter = element?.get_Parameter(analysis.Parameter.Guid);
            if (parameter is null)
            {
                throw new InvalidOperationException(
                    $"Параметр отсутствует у элемента {action.ElementId} перед снятием глобальной ассоциации.");
            }

            parameter.DissociateFromGlobalParameter();
            changedIds.Add(action.ElementId);
        }

        transaction.Commit();
    }

    private void ExecuteFamilyActions(
        Document projectDocument,
        SharedParameterProjectAnalysis analysis,
        IReadOnlyList<FamilyDeletionAction> actions,
        IReadOnlyList<FamilyParameterUsageReport> reports,
        ICollection<long> changedIds,
        ICollection<string> processedFamilies,
        ICollection<string> skippedFamilies,
        CancellationToken cancellationToken)
    {
        Dictionary<long, FamilyParameterUsageReport> reportsByFamilyId = reports
            .Where(report => report.Family.ProjectFamilyId.HasValue)
            .ToDictionary(report => report.Family.ProjectFamilyId!.Value);
        foreach (FamilyDeletionAction action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projectDocument.GetElement(RevitElementIds.Create(action.FamilyId)) is not Family family)
            {
                throw new InvalidOperationException($"Семейство {action.FamilyId} больше не существует.");
            }

            if (!reportsByFamilyId.TryGetValue(action.FamilyId, out FamilyParameterUsageReport? report)
                || report.DeletionBlockers.Count > 0
                || report.Errors.Count > 0)
            {
                skippedFamilies.Add(action.FamilyName);
                throw new InvalidOperationException(
                    $"Семейство «{action.FamilyName}» не имеет актуального безопасного глубокого анализа.");
            }

            Document? familyDocument = null;
            Transaction? familyTransaction = null;
            ProjectFamilySnapshot snapshot = CreateProjectFamilySnapshot(
                projectDocument,
                family);
            try
            {
                familyDocument = projectDocument.EditFamily(family);
                FamilyParameter? familyParameter = versionAdapter.FindFamilyParameter(
                    familyDocument.FamilyManager,
                    analysis.Parameter.Guid);
                if (familyParameter is null)
                {
                    processedFamilies.Add(action.FamilyName);
                    continue;
                }

                familyTransaction = new Transaction(
                    familyDocument,
                    $"TrueBIM: удалить {analysis.Parameter.Name}");
                familyTransaction.Start();
                familyDocument.FamilyManager.RemoveParameter(familyParameter);
                familyDocument.Regenerate();
                familyTransaction.Commit();
                familyTransaction = null;

                Family reloadedFamily = familyDocument.LoadFamily(
                    projectDocument,
                    new PreserveProjectFamilyLoadOptions());
                if (reloadedFamily is null)
                {
                    throw new InvalidOperationException(
                        $"Revit не подтвердил загрузку семейства «{action.FamilyName}».");
                }

                VerifyProjectFamilySnapshot(projectDocument, reloadedFamily, snapshot);
                changedIds.Add(RevitElementIds.GetValue(reloadedFamily.Id));
                processedFamilies.Add(action.FamilyName);
            }
            finally
            {
                if (familyTransaction?.GetStatus() == TransactionStatus.Started)
                {
                    familyTransaction.RollBack();
                }

                if (familyDocument is not null && familyDocument.IsValidObject)
                {
                    familyDocument.Close(false);
                }
            }
        }
    }

    private static void ExecuteParameterDeletion(
        Document document,
        SharedParameterDescriptor parameter,
        ICollection<long> changedIds,
        ICollection<long> deletedIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SharedParameterElement? element = SharedParameterElement.Lookup(document, parameter.Guid);
        if (element is null)
        {
            throw new InvalidOperationException("SharedParameterElement отсутствует перед финальным удалением.");
        }

        Definition? bindingDefinition = FindBindingDefinition(document, parameter);
        using Transaction transaction = new(document, $"TrueBIM: удалить {parameter.Name}");
        transaction.Start();
        if (bindingDefinition is not null)
        {
            if (!document.ParameterBindings.Remove(bindingDefinition))
            {
                throw new InvalidOperationException("Revit не удалил привязку общего параметра.");
            }

            changedIds.Add(parameter.ParameterElementId);
        }

        foreach (ElementId deletedId in document.Delete(element.Id))
        {
            deletedIds.Add(RevitElementIds.GetValue(deletedId));
        }

        transaction.Commit();
    }

    private void VerifyResult(
        Document document,
        SharedParameterProjectAnalysis originalAnalysis,
        SharedParameterDeletionPlan plan,
        IReadOnlyCollection<string> processedFamilies,
        IReadOnlyCollection<long> actualDeletedIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SharedParameterElement.Lookup(document, originalAnalysis.Parameter.Guid) is not null)
        {
            throw new InvalidOperationException("Контрольная проверка обнаружила SharedParameterElement после удаления.");
        }

        if (FindBindingDefinition(document, originalAnalysis.Parameter) is not null)
        {
            throw new InvalidOperationException("Контрольная проверка обнаружила binding после удаления.");
        }

        if (analysisService.CollectScheduleUsages(document, originalAnalysis.Parameter, cancellationToken).Count > 0)
        {
            throw new InvalidOperationException("Контрольная проверка обнаружила поле параметра в спецификации.");
        }

        if (analysisService.CollectViewFilterUsages(document, originalAnalysis.Parameter, cancellationToken).Count > 0)
        {
            throw new InvalidOperationException("Контрольная проверка обнаружила правило параметра в фильтре вида.");
        }

        foreach (ScheduleDeletionAction action in plan.Schedules)
        {
            if (document.GetElement(RevitElementIds.Create(action.ScheduleId)) is not ViewSchedule schedule)
            {
                throw new InvalidOperationException(
                    $"Контрольная проверка не нашла спецификацию {action.ScheduleId}.");
            }

            ScheduleFieldUsage usage = originalAnalysis.ScheduleFields.First(field =>
                field.ScheduleId == action.ScheduleId && field.FieldId == action.FieldId);
            ScheduleDefinition definition = usage.IsEmbeddedDefinition
                ? schedule.Definition.EmbeddedDefinition
                : schedule.Definition;
            HashSet<int> fieldIds = definition
                .GetFieldOrder()
                .Select(fieldId => fieldId.IntegerValue)
                .ToHashSet();
            if (definition.GetFilters().Any(filter => !fieldIds.Contains(filter.FieldId.IntegerValue))
                || definition.GetSortGroupFields().Any(sort => !fieldIds.Contains(sort.FieldId.IntegerValue)))
            {
                throw new InvalidOperationException(
                    $"Спецификация «{schedule.Name}» содержит ссылку на отсутствующий FieldId.");
            }
        }

        HashSet<long> processedFamilyIds = plan.Families
            .Where(action => processedFamilies.Contains(
                action.FamilyName,
                StringComparer.CurrentCultureIgnoreCase))
            .Select(action => action.FamilyId)
            .ToHashSet();
        foreach (ProjectFamilyPresence family in originalAnalysis.Families.Where(family =>
                     family.ContainsParameter && processedFamilyIds.Contains(family.FamilyId)))
        {
            if (document.GetElement(RevitElementIds.Create(family.FamilyId)) is not Family projectFamily)
            {
                throw new InvalidOperationException(
                    $"Контрольная проверка не нашла семейство «{family.FamilyName}».");
            }

            ProjectFamilyPresence verification = familyAnalysisService.CheckProjectFamilyPresence(
                document,
                projectFamily,
                originalAnalysis.Parameter.Guid);
            if (verification.ContainsParameter || verification.Status != FamilyPresenceStatus.NotFound)
            {
                throw new InvalidOperationException(
                    $"Контрольная проверка не подтвердила удаление параметра из семейства «{family.FamilyName}».");
            }
        }

        HashSet<long> plannedIds = plan.DryRunDeletedIds.ToHashSet();
        plannedIds.Add(originalAnalysis.Parameter.ParameterElementId);
        plannedIds.UnionWith(originalAnalysis.ViewFilters
            .Where(filter => filter.OtherRules.Count == 0)
            .Select(filter => filter.FilterId));
        IReadOnlyList<long> unknownActualIds = actualDeletedIds
            .Where(id => !plannedIds.Contains(id))
            .Distinct()
            .ToList();
        if (unknownActualIds.Count > 0)
        {
            throw new InvalidOperationException(
                $"Фактическое удаление обнаружило неизвестные каскадные ElementId: {string.Join(", ", unknownActualIds)}.");
        }

        if (!document.IsValidObject)
        {
            throw new InvalidOperationException(
                "Проектный документ стал недействительным после удаления.");
        }
    }

    private static ProjectFamilySnapshot CreateProjectFamilySnapshot(
        Document projectDocument,
        Family family)
    {
        IReadOnlyList<string> typeNames = family
            .GetFamilySymbolIds()
            .Select(projectDocument.GetElement)
            .OfType<FamilySymbol>()
            .Select(symbol => symbol.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        int instanceCount = new FilteredElementCollector(projectDocument)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Count(instance => instance.Symbol?.Family?.Id == family.Id);
        int sameNameFamilyCount = new FilteredElementCollector(projectDocument)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .Count(candidate => string.Equals(
                candidate.Name,
                family.Name,
                StringComparison.CurrentCultureIgnoreCase));
        return new ProjectFamilySnapshot(
            family.Name,
            typeNames,
            instanceCount,
            sameNameFamilyCount);
    }

    private static void VerifyProjectFamilySnapshot(
        Document projectDocument,
        Family family,
        ProjectFamilySnapshot snapshot)
    {
        if (!string.Equals(family.Name, snapshot.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"После загрузки изменилось имя семейства «{snapshot.Name}».");
        }

        IReadOnlyList<string> typeNames = family
            .GetFamilySymbolIds()
            .Select(projectDocument.GetElement)
            .OfType<FamilySymbol>()
            .Select(symbol => symbol.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (!typeNames.SequenceEqual(
                snapshot.TypeNames,
                StringComparer.CurrentCultureIgnoreCase))
        {
            throw new InvalidOperationException(
                $"После загрузки изменился набор типов семейства «{snapshot.Name}».");
        }

        int instanceCount = new FilteredElementCollector(projectDocument)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Count(instance => instance.Symbol?.Family?.Id == family.Id);
        if (instanceCount != snapshot.InstanceCount)
        {
            throw new InvalidOperationException(
                $"После загрузки изменилось количество экземпляров семейства «{snapshot.Name}»: "
                + $"{snapshot.InstanceCount} → {instanceCount}.");
        }

        int sameNameFamilyCount = new FilteredElementCollector(projectDocument)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .Count(candidate => string.Equals(
                candidate.Name,
                snapshot.Name,
                StringComparison.CurrentCultureIgnoreCase));
        if (sameNameFamilyCount != snapshot.SameNameFamilyCount)
        {
            throw new InvalidOperationException(
                $"После загрузки появились дубликаты семейства «{snapshot.Name}».");
        }
    }

    private static Definition? FindBindingDefinition(
        Document document,
        SharedParameterDescriptor parameter)
    {
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            Definition definition = iterator.Key;
            if (definition is InternalDefinition internalDefinition
                && RevitElementIds.GetValue(internalDefinition.Id) == parameter.ParameterElementId)
            {
                return definition;
            }

            if (definition is ExternalDefinition externalDefinition
                && externalDefinition.GUID == parameter.Guid)
            {
                return definition;
            }
        }

        return null;
    }

    private static HashSet<long> BuildDiscoveredElementIdSet(
        SharedParameterProjectAnalysis analysis)
    {
        HashSet<long> ids =
        [
            analysis.Parameter.ParameterElementId
        ];
        ids.UnionWith(analysis.ScheduleFields.Select(field => field.ScheduleId));
        ids.UnionWith(analysis.ViewFilters.Select(filter => filter.FilterId));
        ids.UnionWith(analysis.GlobalParameterAssociations.Select(association => association.ElementId));
        ids.UnionWith(analysis.GlobalParameterAssociations.Select(association => association.GlobalParameterId));
        ids.UnionWith(analysis.Families.Select(family => family.FamilyId));
        return ids;
    }

    private static DryRunDeletedElement CreateDryRunElement(
        Document document,
        ElementId id,
        ISet<long> discoveredIds)
    {
        long value = RevitElementIds.GetValue(id);
        Element? element = document.GetElement(id);
        return new DryRunDeletedElement(
            value,
            element?.GetType().Name ?? "Unknown",
            element?.Name ?? string.Empty,
            discoveredIds.Contains(value));
    }

    private static SharedParameterDeletionResult CreateResult(
        Document document,
        SharedParameterProjectAnalysis analysis,
        SharedParameterDeletionPlan plan,
        string userName,
        DeletionMode mode,
        DeletionStatus status,
        IReadOnlyList<long> changedIds,
        IReadOnlyList<long> deletedIds,
        IReadOnlyList<string> processedFamilies,
        IReadOnlyList<string> skippedFamilies,
        IReadOnlyList<AnalysisError> errors,
        string summary)
    {
        return new SharedParameterDeletionResult(
            new DocumentIdentity(
                document.Title,
                document.PathName ?? string.Empty,
                document.Application.VersionNumber,
                document.IsFamilyDocument,
                document.IsWorkshared),
            analysis.Parameter,
            DateTimeOffset.Now,
            userName,
            mode,
            status,
            plan,
            changedIds.Distinct().ToList(),
            deletedIds.Distinct().ToList(),
            processedFamilies.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
            skippedFamilies.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList(),
            errors,
            summary);
    }

    private sealed class PreserveProjectFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = false;
            return true;
        }

        public bool OnSharedFamilyFound(
            Family sharedFamily,
            bool familyInUse,
            out FamilySource source,
            out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = false;
            return true;
        }
    }

    private sealed record ProjectFamilySnapshot(
        string Name,
        IReadOnlyList<string> TypeNames,
        int InstanceCount,
        int SameNameFamilyCount);
}
