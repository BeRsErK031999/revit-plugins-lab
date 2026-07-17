namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum FinishRoomScheduleAction
{
    Create,
    Update,
    NoChanges,
    Blocked
}

public sealed record FinishRoomScheduleColumn(
    ParameterReference Parameter,
    string Heading,
    double WidthMillimeters);

public sealed record FinishRoomScheduleScopeFilter(
    ReportScopeKind Kind,
    ParameterReference? Parameter,
    ParameterStorageKind StorageKind,
    string RawValue)
{
    public static FinishRoomScheduleScopeFilter EntireProject()
    {
        return new FinishRoomScheduleScopeFilter(
            ReportScopeKind.EntireProject,
            null,
            ParameterStorageKind.None,
            string.Empty);
    }
}

public sealed class FinishRoomSchedulePlan
{
    public FinishRoomSchedulePlan(
        string scheduleName,
        IEnumerable<FinishRoomScheduleColumn> columns,
        FinishRoomScheduleScopeFilter scopeFilter,
        string settingsHash,
        IEnumerable<string> parameterIdentities)
    {
        if (string.IsNullOrWhiteSpace(scheduleName))
        {
            throw new ArgumentException("Schedule name must not be empty.", nameof(scheduleName));
        }

        if (string.IsNullOrWhiteSpace(settingsHash))
        {
            throw new ArgumentException("Settings hash must not be empty.", nameof(settingsHash));
        }

        ScheduleName = scheduleName.Trim();
        Columns = (columns ?? throw new ArgumentNullException(nameof(columns))).ToArray();
        ScopeFilter = scopeFilter ?? throw new ArgumentNullException(nameof(scopeFilter));
        SettingsHash = settingsHash;
        ParameterIdentities = (parameterIdentities ?? throw new ArgumentNullException(nameof(parameterIdentities)))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToArray();

        if (Columns.Count == 0)
        {
            throw new ArgumentException("At least one schedule column is required.", nameof(columns));
        }
    }

    public string ScheduleName { get; }

    public IReadOnlyList<FinishRoomScheduleColumn> Columns { get; }

    public FinishRoomScheduleScopeFilter ScopeFilter { get; }

    public string SettingsHash { get; }

    public IReadOnlyList<string> ParameterIdentities { get; }
}

public sealed class FinishRoomSchedulePreflight
{
    public FinishRoomSchedulePreflight(
        FinishRoomSchedulePlan? plan,
        FinishRoomScheduleAction action,
        long? scheduleId,
        IEnumerable<FinishWriteIssue> issues)
    {
        Plan = plan;
        Action = action;
        ScheduleId = scheduleId;
        Issues = FinishWriteOrdering.OrderIssues(issues);
    }

    public FinishRoomSchedulePlan? Plan { get; }

    public FinishRoomScheduleAction Action { get; }

    public long? ScheduleId { get; }

    public IReadOnlyList<FinishWriteIssue> Issues { get; }

    public bool RequiresTransaction => Action is FinishRoomScheduleAction.Create or FinishRoomScheduleAction.Update;

    public static FinishRoomSchedulePreflight Blocked(FinishWriteIssue issue)
    {
        return new FinishRoomSchedulePreflight(null, FinishRoomScheduleAction.Blocked, null, [issue]);
    }
}

public sealed record FinishRoomScheduleApplyResult(
    long ScheduleId,
    string ScheduleName,
    FinishRoomScheduleAction Action);

public sealed record FinishScheduleMetadata(
    int SchemaVersion,
    string FeatureId,
    string SettingsHash,
    string Scope,
    IReadOnlyList<string> ParameterIdentities,
    string LastUpdatedUtc);
