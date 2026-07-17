namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum FinishWriteIssueSeverity
{
    Critical,
    Warning
}

public enum FinishWriteIssueCode
{
    DocumentReadOnly,
    FamilyDocument,
    DocumentAlreadyModifiable,
    NoTargetRooms,
    OutputConfigurationInvalid,
    TargetElementMissing,
    TargetElementLocked,
    ParameterMissing,
    ParameterReadOnly,
    ParameterStorageMismatch,
    UnassignedOwnership,
    UnknownOwnership,
    ValueChangedAfterPreview,
    ScheduleNameConflict,
    WriteRejected,
    WriteFailed
}

public sealed record FinishWriteIssue(
    FinishWriteIssueCode Code,
    FinishWriteIssueSeverity Severity,
    string Message,
    long? ElementId = null,
    string? Role = null);

public sealed record FinishParameterTargetValue
{
    public FinishParameterTargetValue(
        long elementId,
        ParameterReference reference,
        string role,
        string value,
        bool isRequired,
        FinishPreviewCategory? category = null)
    {
        if (elementId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementId));
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Parameter role must not be empty.", nameof(role));
        }

        ElementId = elementId;
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        Role = role;
        Value = value ?? string.Empty;
        IsRequired = isRequired;
        Category = category;
    }

    public long ElementId { get; }

    public ParameterReference Reference { get; }

    public string Role { get; }

    public string Value { get; }

    public bool IsRequired { get; }

    public FinishPreviewCategory? Category { get; }
}

public sealed class FinishParameterTargetBuildResult
{
    public FinishParameterTargetBuildResult(
        int targetElementCount,
        IEnumerable<FinishParameterTargetValue> targets,
        IEnumerable<FinishWriteIssue> issues)
    {
        if (targetElementCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetElementCount));
        }

        TargetElementCount = targetElementCount;
        Targets = (targets ?? throw new ArgumentNullException(nameof(targets)))
            .OrderBy(target => target.ElementId)
            .ThenBy(target => target.Role, StringComparer.Ordinal)
            .ThenBy(target => target.Reference.StableKey, StringComparer.Ordinal)
            .ToArray();
        Issues = FinishWriteOrdering.OrderIssues(issues);
    }

    public int TargetElementCount { get; }

    public IReadOnlyList<FinishParameterTargetValue> Targets { get; }

    public IReadOnlyList<FinishWriteIssue> Issues { get; }
}

public sealed record FinishParameterWriteCandidate
{
    public FinishParameterWriteCandidate(
        FinishParameterTargetValue target,
        string currentValue,
        FinishWriteIssueCode? blockingCode = null,
        string? blockingMessage = null)
    {
        if (blockingCode.HasValue != !string.IsNullOrWhiteSpace(blockingMessage))
        {
            throw new ArgumentException("Blocking code and message must be provided together.");
        }

        Target = target ?? throw new ArgumentNullException(nameof(target));
        CurrentValue = currentValue ?? string.Empty;
        BlockingCode = blockingCode;
        BlockingMessage = blockingMessage;
    }

    public FinishParameterTargetValue Target { get; }

    public string CurrentValue { get; }

    public FinishWriteIssueCode? BlockingCode { get; }

    public string? BlockingMessage { get; }
}

public sealed record FinishParameterChange(
    long ElementId,
    ParameterReference Reference,
    string Role,
    string PreviousValue,
    string NewValue,
    bool IsRequired,
    FinishPreviewCategory? Category = null);

public sealed class FinishWritePlan
{
    public FinishWritePlan(
        int targetElementCount,
        int candidateCount,
        int unchangedCount,
        int blockedCount,
        IEnumerable<FinishParameterChange> changes,
        IEnumerable<FinishWriteIssue> issues)
    {
        if (targetElementCount < 0 || candidateCount < 0 || unchangedCount < 0 || blockedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetElementCount));
        }

        TargetElementCount = targetElementCount;
        CandidateCount = candidateCount;
        UnchangedCount = unchangedCount;
        BlockedCount = blockedCount;
        Changes = (changes ?? throw new ArgumentNullException(nameof(changes)))
            .OrderBy(change => change.ElementId)
            .ThenBy(change => change.Role, StringComparer.Ordinal)
            .ThenBy(change => change.Reference.StableKey, StringComparer.Ordinal)
            .ToArray();
        Issues = FinishWriteOrdering.OrderIssues(issues);
    }

    public int TargetElementCount { get; }

    public int CandidateCount { get; }

    public int UnchangedCount { get; }

    public int BlockedCount { get; }

    public IReadOnlyList<FinishParameterChange> Changes { get; }

    public IReadOnlyList<FinishWriteIssue> Issues { get; }

    public bool HasCriticalIssues => Issues.Any(issue => issue.Severity == FinishWriteIssueSeverity.Critical);

    public static FinishWritePlan Empty(IEnumerable<FinishWriteIssue>? issues = null)
    {
        return new FinishWritePlan(0, 0, 0, 0, [], issues ?? []);
    }
}

public sealed class FinishScheduleWritePreview
{
    public FinishScheduleWritePreview(
        int groupCount,
        int roomCount,
        FinishWritePlan roomPlan,
        FinishWritePlan ownershipPlan,
        IEnumerable<string> calculationWarnings,
        FinishRoomSchedulePreflight? schedule = null,
        FinishSchedulePreviewResult? calculation = null)
    {
        if (groupCount < 0 || roomCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(groupCount));
        }

        GroupCount = groupCount;
        RoomCount = roomCount;
        RoomPlan = roomPlan ?? throw new ArgumentNullException(nameof(roomPlan));
        OwnershipPlan = ownershipPlan ?? throw new ArgumentNullException(nameof(ownershipPlan));
        Schedule = schedule ?? new FinishRoomSchedulePreflight(
            null,
            FinishRoomScheduleAction.NoChanges,
            null,
            []);
        Calculation = calculation;
        CalculationWarnings = (calculationWarnings ?? throw new ArgumentNullException(nameof(calculationWarnings)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Issues = FinishWriteOrdering.OrderIssues(
            RoomPlan.Issues.Concat(OwnershipPlan.Issues).Concat(Schedule.Issues));
    }

    public int GroupCount { get; }

    public int RoomCount { get; }

    public FinishWritePlan RoomPlan { get; }

    public FinishWritePlan OwnershipPlan { get; }

    public FinishRoomSchedulePreflight Schedule { get; }

    public FinishSchedulePreviewResult? Calculation { get; }

    public IReadOnlyList<string> CalculationWarnings { get; }

    public IReadOnlyList<FinishWriteIssue> Issues { get; }

    public int TotalChangeCount => RoomPlan.Changes.Count + OwnershipPlan.Changes.Count;

    public bool RequiresTransaction => TotalChangeCount > 0 || Schedule.RequiresTransaction;

    public bool CanApply => !Issues.Any(issue => issue.Severity == FinishWriteIssueSeverity.Critical);

    public static FinishScheduleWritePreview Blocked(FinishWriteIssue issue)
    {
        if (issue is null)
        {
            throw new ArgumentNullException(nameof(issue));
        }

        return new FinishScheduleWritePreview(
            0,
            0,
            FinishWritePlan.Empty([issue]),
            FinishWritePlan.Empty(),
            []);
    }
}

public enum FinishScheduleWriteStatus
{
    Applied,
    NoChanges,
    Blocked,
    Failed
}

public sealed record FinishScheduleWriteResult(
    FinishScheduleWriteStatus Status,
    int AppliedRoomValues,
    int AppliedOwnershipValues,
    int SkippedOwnershipValues,
    IReadOnlyList<string> Warnings,
    string Message,
    FinishRoomScheduleApplyResult? Schedule = null,
    FinishSchedulePerformanceSummary? Performance = null)
{
    public bool Succeeded => Status is FinishScheduleWriteStatus.Applied or FinishScheduleWriteStatus.NoChanges;
}

public sealed record FinishOwnershipApplyResult(
    int AppliedCount,
    int SkippedCount,
    IReadOnlyList<string> Warnings);

public sealed record FinishScheduleCalculationResult(
    FinishSchedulePreviewResult Preview,
    FinishElementCollection Collection,
    FinishSchedulePreviewBuild Build,
    FinishQuantityResult Quantities,
    RoomFinishSnapshotBuildResult? RoomSnapshots,
    FinishAggregationResult? Aggregation);

internal static class FinishWriteOrdering
{
    public static IReadOnlyList<FinishWriteIssue> OrderIssues(IEnumerable<FinishWriteIssue> issues)
    {
        return (issues ?? throw new ArgumentNullException(nameof(issues)))
            .OrderBy(issue => issue.Severity)
            .ThenBy(issue => issue.ElementId ?? long.MaxValue)
            .ThenBy(issue => issue.Role, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code)
            .ToArray();
    }
}
