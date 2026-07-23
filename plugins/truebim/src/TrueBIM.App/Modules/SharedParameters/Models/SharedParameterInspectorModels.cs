namespace TrueBIM.App.Modules.SharedParameters.Models;

public enum SharedParameterBindingKind
{
    None,
    Instance,
    Type
}

public enum DetectionConfidence
{
    Exact,
    Probable,
    Partial,
    ManualCheckRequired,
    Unsupported
}

public enum FamilyPresenceStatus
{
    Found,
    NotFound,
    Unsupported,
    CannotOpen,
    Cancelled,
    Failed
}

public enum FamilySourceKind
{
    ActiveProject,
    Folder,
    SelectedFiles,
    CurrentFamily
}

public enum SharedParameterListFilter
{
    All,
    Instance,
    Type,
    Bound,
    Unbound,
    UsedInSchedules,
    UsedInViewFilters,
    PresentInFamilies,
    Unused
}

public enum DeletionMode
{
    Safe,
    Advanced
}

public enum DeletionStatus
{
    Success,
    CompletedWithWarnings,
    Blocked,
    RolledBack,
    Failed,
    Cancelled
}

public enum DeletionRisk
{
    Low,
    Medium,
    High,
    Blocking
}

public enum DeletionActionSupport
{
    Supported,
    PartiallySupported,
    Unsupported
}

public sealed record DocumentIdentity(
    string Title,
    string Path,
    string RevitVersion,
    bool IsFamilyDocument,
    bool IsWorkshared);

public sealed record CategoryDescriptor(
    long ElementId,
    string Name);

public sealed record SharedParameterDescriptor(
    long ParameterElementId,
    Guid Guid,
    string Name,
    string DataTypeName,
    string ParameterGroupName,
    SharedParameterBindingKind BindingKind,
    IReadOnlyList<CategoryDescriptor> Categories,
    bool HasProjectBinding,
    bool VariesAcrossGroups)
{
    public string ShortGuid => Guid.ToString("D").Substring(0, 8);

    public string BindingDisplay => BindingKind switch
    {
        SharedParameterBindingKind.Instance => "Экземпляр",
        SharedParameterBindingKind.Type => "Тип",
        _ => "Без привязки"
    };

    public string IdentityKey => $"{ParameterElementId}:{Guid:D}";
}

public sealed record ElementParameterUsage(
    long ElementId,
    string UniqueId,
    string Name,
    string CategoryName,
    string FamilyName,
    string TypeName,
    bool IsElementType,
    bool HasParameter,
    bool HasValue,
    bool IsReadOnly,
    string? DisplayValue);

public sealed record ElementUsageAggregate(
    string CategoryName,
    int ElementCount,
    int HasParameterCount,
    int FilledCount,
    int EmptyCount,
    int ReadOnlyCount);

public sealed record ScheduleFieldUsage(
    long ScheduleId,
    string ScheduleName,
    int FieldId,
    string FieldName,
    string ColumnHeading,
    int Position,
    bool IsHidden,
    bool UsedInFilter,
    bool UsedInSortOrGroup,
    bool IsEmbeddedDefinition,
    bool HasCalculatedOrCombinedDependencies,
    DetectionConfidence Confidence);

public sealed record FilterRuleDescriptor(
    long ParameterElementId,
    string ParameterName,
    string Operator,
    string Value,
    string ValueType,
    bool IsInverted,
    DetectionConfidence Confidence,
    string RuleClassName);

public sealed record FilterTreeNodeDescriptor(
    string Operator,
    IReadOnlyList<FilterTreeNodeDescriptor> Children,
    IReadOnlyList<FilterRuleDescriptor> Rules,
    DetectionConfidence Confidence);

public sealed record AppliedViewFilterUsage(
    long ViewId,
    string ViewName,
    string ViewType,
    bool IsTemplate,
    bool IsVisible,
    bool HasGraphicOverrides,
    bool IsPlacedOnSheet);

public sealed record ViewFilterUsage(
    long FilterId,
    string FilterName,
    IReadOnlyList<CategoryDescriptor> Categories,
    FilterTreeNodeDescriptor RuleTree,
    IReadOnlyList<FilterRuleDescriptor> TargetRules,
    IReadOnlyList<FilterRuleDescriptor> OtherRules,
    IReadOnlyList<AppliedViewFilterUsage> AppliedViews,
    bool CanRebuildWithoutTarget,
    DetectionConfidence Confidence);

public sealed record GlobalParameterAssociationUsage(
    long ElementId,
    string ElementName,
    string ElementCategory,
    long GlobalParameterId,
    string GlobalParameterName,
    string Formula,
    bool IsReporting,
    int ControlledObjectCount);

public sealed record ProjectFamilyPresence(
    long FamilyId,
    string FamilyName,
    string CategoryName,
    FamilyPresenceStatus Status,
    bool ContainsParameter,
    string? ErrorMessage);

public sealed record FamilyDocumentDescriptor(
    string Name,
    string Path,
    string CategoryName,
    FamilySourceKind SourceKind,
    long? ProjectFamilyId);

public sealed record FamilySharedParameterCatalogScan(
    FamilyDocumentDescriptor Family,
    IReadOnlyList<SharedParameterDescriptor> Parameters,
    IReadOnlyList<AnalysisError> Errors);

public sealed record FamilyParameterDescriptor(
    string Name,
    Guid Guid,
    string DataTypeName,
    string ParameterGroupName,
    bool IsShared,
    bool IsInstance,
    bool IsReporting,
    string Formula,
    int TypeCount,
    int FilledTypeCount,
    int EmptyTypeCount);

public sealed record FamilyTypeValueUsage(
    string TypeName,
    bool HasValue,
    string DisplayValue,
    string InternalValue,
    bool IsFormulaDriven);

public sealed record FormulaUsage(
    string ParameterName,
    string Formula,
    bool IsTargetFormula,
    DetectionConfidence Confidence);

public sealed record DimensionUsage(
    long DimensionId,
    string ViewName,
    int SegmentCount,
    bool IsReporting,
    string Value,
    DetectionConfidence Confidence);

public sealed record AssociatedParameterUsage(
    long ElementId,
    string ElementName,
    string CategoryName,
    string ParameterName,
    string ElementTypeName,
    string Direction,
    DetectionConfidence Confidence);

public sealed record NestedFamilyUsage(
    long ElementId,
    string FamilyName,
    string TypeName,
    string ParameterName,
    string AssociationKind,
    int Depth,
    DetectionConfidence Confidence);

public sealed record AnnotationUsage(
    string FamilyName,
    string CategoryName,
    DetectionConfidence Confidence,
    string Message);

public sealed record DeletionBlocker(
    string Code,
    string Message,
    string ObjectKind,
    long? ElementId,
    DetectionConfidence Confidence);

public sealed record DeletionWarning(
    string Code,
    string Message,
    string ObjectKind,
    long? ElementId);

public sealed record AnalysisError(
    string Phase,
    string Message,
    string? Source);

public sealed record FamilyParameterUsageReport(
    FamilyDocumentDescriptor Family,
    Guid TargetGuid,
    bool ParameterFound,
    FamilyParameterDescriptor? Parameter,
    IReadOnlyList<FamilyTypeValueUsage> TypeValues,
    IReadOnlyList<FormulaUsage> Formulas,
    IReadOnlyList<DimensionUsage> Dimensions,
    IReadOnlyList<AssociatedParameterUsage> Associations,
    IReadOnlyList<NestedFamilyUsage> NestedFamilies,
    IReadOnlyList<AnnotationUsage> Annotations,
    IReadOnlyList<DeletionBlocker> DeletionBlockers,
    IReadOnlyList<AnalysisError> Errors);

public sealed record SharedParameterProjectAnalysis(
    DocumentIdentity Document,
    SharedParameterDescriptor Parameter,
    DateTimeOffset AnalyzedAt,
    IReadOnlyList<ElementParameterUsage> Elements,
    IReadOnlyList<ElementUsageAggregate> ElementAggregates,
    IReadOnlyList<ScheduleFieldUsage> ScheduleFields,
    IReadOnlyList<ViewFilterUsage> ViewFilters,
    IReadOnlyList<GlobalParameterAssociationUsage> GlobalParameterAssociations,
    IReadOnlyList<ProjectFamilyPresence> Families,
    IReadOnlyList<DeletionBlocker> Blockers,
    IReadOnlyList<DeletionWarning> Warnings,
    IReadOnlyList<AnalysisError> Errors)
{
    public int FilledValueCount => Elements.Count(element => element.HasParameter && element.HasValue);

    public int EmptyValueCount => Elements.Count(element => element.HasParameter && !element.HasValue);

    public int FamilyCountWithParameter => Families.Count(family => family.ContainsParameter);

    public bool HasAnyUsage => Elements.Any(element => element.HasParameter)
        || ScheduleFields.Count > 0
        || ViewFilters.Count > 0
        || GlobalParameterAssociations.Count > 0
        || FamilyCountWithParameter > 0;
}

public sealed record DryRunDeletedElement(
    long ElementId,
    string ElementType,
    string ElementName,
    bool WasDiscoveredByAnalysis);

public sealed record SharedParameterDryRunResult(
    IReadOnlyList<DryRunDeletedElement> DeletedElements,
    bool ParameterRestoredAfterRollback,
    IReadOnlyList<DeletionBlocker> Blockers,
    IReadOnlyList<AnalysisError> Errors);

public sealed record ScheduleDeletionAction(
    long ScheduleId,
    string ScheduleName,
    int FieldId,
    string Action,
    string Reason,
    DeletionRisk Risk,
    bool CanRollback,
    DeletionActionSupport Support);

public sealed record ViewFilterDeletionAction(
    long FilterId,
    string FilterName,
    string Action,
    string Reason,
    DeletionRisk Risk,
    bool CanRollback,
    DeletionActionSupport Support);

public sealed record GlobalParameterDeletionAction(
    long ElementId,
    long GlobalParameterId,
    string GlobalParameterName,
    string Action,
    string Reason,
    DeletionRisk Risk,
    bool CanRollback,
    DeletionActionSupport Support);

public sealed record FamilyDeletionAction(
    long FamilyId,
    string FamilyName,
    string Action,
    string Reason,
    DeletionRisk Risk,
    bool CanRollback,
    DeletionActionSupport Support);

public sealed record SharedParameterDeletionPlan(
    SharedParameterDescriptor Parameter,
    IReadOnlyList<ScheduleDeletionAction> Schedules,
    IReadOnlyList<ViewFilterDeletionAction> ViewFilters,
    IReadOnlyList<GlobalParameterDeletionAction> GlobalParameters,
    IReadOnlyList<FamilyDeletionAction> Families,
    IReadOnlyList<long> DryRunDeletedIds,
    IReadOnlyList<DeletionBlocker> Blockers,
    IReadOnlyList<DeletionWarning> Warnings,
    bool CanExecuteSafely);

public sealed record SharedParameterDeletionResult(
    DocumentIdentity Document,
    SharedParameterDescriptor Parameter,
    DateTimeOffset CompletedAt,
    string UserName,
    DeletionMode Mode,
    DeletionStatus Status,
    SharedParameterDeletionPlan Plan,
    IReadOnlyList<long> ChangedElementIds,
    IReadOnlyList<long> DeletedElementIds,
    IReadOnlyList<string> ProcessedFamilies,
    IReadOnlyList<string> SkippedFamilies,
    IReadOnlyList<AnalysisError> Errors,
    string Summary);

public sealed record SharedParameterReportPackage(
    string JsonPath,
    string CsvPath,
    string HtmlPath,
    string TextPath);
