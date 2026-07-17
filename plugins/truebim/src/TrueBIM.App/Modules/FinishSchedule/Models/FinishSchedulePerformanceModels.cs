namespace TrueBIM.App.Modules.FinishSchedule.Models;

public static class FinishScheduleStageNames
{
    public const string CollectCandidates = "collect_candidates";
    public const string ScopeAndIndex = "scope_and_index";
    public const string PhysicalQuantities = "physical_quantities";
    public const string Aggregation = "aggregation";
    public const string TotalCalculation = "total_calculation";
    public const string OwnershipWrite = "ownership_write";
    public const string RoomWrite = "room_write";
    public const string ScheduleWrite = "schedule_write";
    public const string TotalApply = "total_apply";
}

public sealed record FinishScheduleStageTiming
{
    public FinishScheduleStageTiming(string stage, long elapsedMilliseconds)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("Stage name must not be empty.", nameof(stage));
        }

        if (elapsedMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        }

        Stage = stage;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    public string Stage { get; }

    public long ElapsedMilliseconds { get; }
}

public sealed record FinishGeometryCacheMetrics(
    int RoomRequests,
    int RoomEntries,
    int RoomHits,
    int ElementRequests,
    int ElementEntries,
    int ElementHits)
{
    public static FinishGeometryCacheMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0);
}

public sealed record FinishScheduleCacheSummary(
    int TypeEntries,
    FinishGeometryCacheMetrics Geometry)
{
    public static FinishScheduleCacheSummary Empty { get; } = new(0, FinishGeometryCacheMetrics.Empty);
}

public sealed class FinishSchedulePerformanceSummary
{
    public FinishSchedulePerformanceSummary(
        IEnumerable<FinishScheduleStageTiming> stages,
        FinishScheduleCacheSummary? cache = null)
    {
        Stages = (stages ?? throw new ArgumentNullException(nameof(stages))).ToArray();
        Cache = cache ?? FinishScheduleCacheSummary.Empty;
    }

    public IReadOnlyList<FinishScheduleStageTiming> Stages { get; }

    public FinishScheduleCacheSummary Cache { get; }
}
