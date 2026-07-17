using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleWritePlanningTests
{
    [Fact]
    public void ChangePlanner_SkipsValuesThatAreAlreadyCurrent()
    {
        FinishParameterTargetValue target = Target(1, "Current", isRequired: true);

        FinishWritePlan plan = new FinishParameterChangePlanner().Create(
            1,
            [new FinishParameterWriteCandidate(target, "Current")]);

        Assert.Empty(plan.Changes);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(0, plan.BlockedCount);
    }

    [Fact]
    public void ChangePlanner_RequiredBlockerIsCritical()
    {
        FinishParameterTargetValue target = Target(1, "Next", isRequired: true);

        FinishWritePlan plan = new FinishParameterChangePlanner().Create(
            1,
            [
                new FinishParameterWriteCandidate(
                    target,
                    string.Empty,
                    FinishWriteIssueCode.ParameterReadOnly,
                    "Read-only room parameter.")
            ]);

        Assert.True(plan.HasCriticalIssues);
        Assert.Equal(FinishWriteIssueSeverity.Critical, Assert.Single(plan.Issues).Severity);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void ChangePlanner_OptionalBlockerIsWarning()
    {
        FinishParameterTargetValue target = Target(1, "Next", isRequired: false);

        FinishWritePlan plan = new FinishParameterChangePlanner().Create(
            1,
            [
                new FinishParameterWriteCandidate(
                    target,
                    string.Empty,
                    FinishWriteIssueCode.TargetElementLocked,
                    "Locked ownership element.")
            ]);

        Assert.False(plan.HasCriticalIssues);
        Assert.Equal(FinishWriteIssueSeverity.Warning, Assert.Single(plan.Issues).Severity);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void ChangePlanner_IsDeterministicRegardlessOfCandidateOrder()
    {
        FinishParameterWriteCandidate[] candidates =
        [
            new FinishParameterWriteCandidate(Target(10, "B", isRequired: true), "A"),
            new FinishParameterWriteCandidate(Target(2, "D", isRequired: true), "C")
        ];
        FinishParameterChangePlanner planner = new();

        FinishWritePlan first = planner.Create(2, candidates);
        FinishWritePlan second = planner.Create(2, candidates.Reverse());

        Assert.Equal(first.Changes, second.Changes);
        Assert.Equal([2L, 10L], first.Changes.Select(change => change.ElementId));
    }

    [Fact]
    public void RoomValueBuilder_CreatesIdenticalTargetsForEveryRoomInGroup()
    {
        FinishScheduleSettings settings = Settings(writeOwnership: false);
        FinishAggregationResult aggregation = Aggregation([2, 1]);

        FinishParameterTargetBuildResult result = new RoomFinishWriteValueBuilder().Build(
            settings,
            aggregation);

        Assert.Equal(2, result.TargetElementCount);
        Assert.Equal(6, result.Targets.Count);
        Assert.Empty(result.Issues);
        string[] firstValues = result.Targets
            .Where(target => target.ElementId == 1)
            .Select(target => target.Value)
            .ToArray();
        string[] secondValues = result.Targets
            .Where(target => target.ElementId == 2)
            .Select(target => target.Value)
            .ToArray();
        Assert.Equal(firstValues, secondValues);
    }

    [Fact]
    public void RoomValueBuilder_DoesNotCreateTargetsForDisabledCategories()
    {
        FinishParameterTargetBuildResult result = new RoomFinishWriteValueBuilder().Build(
            Settings(writeOwnership: false),
            Aggregation([1]));

        Assert.DoesNotContain(result.Targets, target => target.Category == FinishPreviewCategory.Floors);
        Assert.DoesNotContain(result.Targets, target => target.Category == FinishPreviewCategory.Ceilings);
    }

    [Fact]
    public void RoomValueBuilder_MissingRequiredOutputIsCritical()
    {
        FinishScheduleSettings settings = Settings(writeOwnership: false) with
        {
            RoomListOutputParameter = null
        };

        FinishParameterTargetBuildResult result = new RoomFinishWriteValueBuilder().Build(
            settings,
            Aggregation([1]));

        FinishWriteIssue issue = Assert.Single(result.Issues);
        Assert.Equal(FinishWriteIssueSeverity.Critical, issue.Severity);
        Assert.Equal(FinishWriteIssueCode.OutputConfigurationInvalid, issue.Code);
    }

    [Fact]
    public void OwnershipValueBuilder_UsesDistinctNaturallySortedRoomIdentifiers()
    {
        FinishScheduleSettings settings = Settings(writeOwnership: true);
        RoomFinishSnapshotBuildResult rooms = RoomSnapshots(
            (1, "10"),
            (2, "2"),
            (3, "1"),
            (4, "2"));
        FinishQuantityResult quantities = new(
        [
            Occurrence(1, 100),
            Occurrence(2, 100),
            Occurrence(3, 100),
            Occurrence(4, 100)
        ],
        []);

        FinishParameterTargetBuildResult result = new FinishOwnershipValueBuilder().Build(
            settings,
            [Element(100)],
            quantities,
            rooms);

        Assert.Equal("1, 2, 10", Assert.Single(result.Targets).Value);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void OwnershipValueBuilder_UnassignedCandidateIsClearedWithWarning()
    {
        FinishParameterTargetBuildResult result = new FinishOwnershipValueBuilder().Build(
            Settings(writeOwnership: true),
            [Element(100)],
            new FinishQuantityResult([], []),
            RoomSnapshots((1, "101")));

        Assert.Equal(string.Empty, Assert.Single(result.Targets).Value);
        Assert.Equal(FinishWriteIssueCode.UnassignedOwnership, Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void OwnershipValueBuilder_UnknownGeometryNeverClearsExistingOwnership()
    {
        FinishParameterTargetBuildResult result = new FinishOwnershipValueBuilder().Build(
            Settings(writeOwnership: true),
            [Element(100)],
            new FinishQuantityResult(
                [],
                [
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.ProjectedAreaUnavailable,
                        "Unknown geometry.",
                        RoomId: 1,
                        ElementId: 100,
                        Category: FinishPreviewCategory.Walls)
                ]),
            RoomSnapshots((1, "101")));

        Assert.Empty(result.Targets);
        Assert.Equal(FinishWriteIssueCode.UnknownOwnership, Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void OwnershipValueBuilder_DisabledSettingSkipsStageCompletely()
    {
        FinishParameterTargetBuildResult result = new FinishOwnershipValueBuilder().Build(
            Settings(writeOwnership: false),
            [Element(100)],
            new FinishQuantityResult([], []),
            RoomSnapshots((1, "101")));

        Assert.Equal(0, result.TargetElementCount);
        Assert.Empty(result.Targets);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void WritePreview_OptionalWarningsDoNotHideCriticalRoomFailures()
    {
        FinishWritePlan roomPlan = FinishWritePlan.Empty(
        [
            new FinishWriteIssue(
                FinishWriteIssueCode.ParameterMissing,
                FinishWriteIssueSeverity.Critical,
                "Missing room parameter.")
        ]);
        FinishWritePlan ownershipPlan = FinishWritePlan.Empty(
        [
            new FinishWriteIssue(
                FinishWriteIssueCode.TargetElementLocked,
                FinishWriteIssueSeverity.Warning,
                "Locked ownership element.")
        ]);

        FinishScheduleWritePreview preview = new(1, 1, roomPlan, ownershipPlan, []);

        Assert.False(preview.CanApply);
        Assert.Equal(2, preview.Issues.Count);
        Assert.Equal(FinishWriteIssueSeverity.Critical, preview.Issues[0].Severity);
    }

    private static FinishParameterTargetValue Target(long elementId, string value, bool isRequired)
    {
        return new FinishParameterTargetValue(
            elementId,
            ParameterReference.Project(
                "Output",
                10,
                ParameterBindingKind.Instance,
                ParameterStorageKind.String),
            "Output",
            value,
            isRequired);
    }

    private static FinishScheduleSettings Settings(bool writeOwnership)
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        return defaults with
        {
            DescriptionParameter = Reference("Description", 1, ParameterBindingKind.Type),
            WriteOwnership = writeOwnership,
            RoomListOutputParameter = Reference("Room list", 2),
            Walls = defaults.Walls with
            {
                OwnershipParameter = Reference("Wall ownership", 3),
                OutputDescriptionParameter = Reference("Wall description", 4),
                OutputAreaParameter = Reference("Wall area", 5)
            },
            Floors = defaults.Floors with { IsEnabled = false },
            Ceilings = defaults.Ceilings with { IsEnabled = false }
        };
    }

    private static ParameterReference Reference(
        string name,
        long id,
        ParameterBindingKind binding = ParameterBindingKind.Instance)
    {
        return ParameterReference.Project(name, id, binding, ParameterStorageKind.String);
    }

    private static FinishAggregationResult Aggregation(IEnumerable<long> roomIds)
    {
        long[] ids = roomIds.OrderBy(id => id).ToArray();
        FinishAggregatedCategory walls = new(
            true,
            FinishValueState.Resolved,
            [
                new FinishAggregatedItem(
                    new NormalizedFinishDescription("Paint", "PAINT"),
                    12.5)
            ]);
        FinishAggregatedCategory disabled = new(false, FinishValueState.NoFinish, []);
        FinishRoomGroupOutput output = new(
            "101, 102",
            new FinishFormattedCategoryOutput("Paint", "12,50"),
            null,
            null);
        FinishAggregatedGroup group = new(
            new FinishGroupKey("W:<RESOLVED>:5:PAINT"),
            ids,
            ids.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            walls,
            disabled,
            disabled,
            output);
        return new FinishAggregationResult([group], []);
    }

    private static RoomFinishSnapshotBuildResult RoomSnapshots(params (long Id, string Identifier)[] rooms)
    {
        RoomFinishCategorySnapshot disabled = new(false, FinishValueState.NoFinish, []);
        return new RoomFinishSnapshotBuildResult(
            rooms.Select(room => new RoomFinishSnapshot(
                room.Id,
                room.Identifier,
                disabled,
                disabled,
                disabled)),
            []);
    }

    private static FinishClassifiedElement Element(long id)
    {
        return new FinishClassifiedElement(
            new FinishElementCandidateSnapshot(
                id,
                id + 1000,
                FinishPhysicalCategory.Wall,
                new AxisAlignedBox3D(0, 0, 0, 1, 1, 1)),
            FinishPreviewCategory.Walls);
    }

    private static FinishOccurrence Occurrence(long roomId, long elementId)
    {
        return new FinishOccurrence(
            roomId,
            elementId,
            FinishPreviewCategory.Walls,
            1,
            FinishQuantityMethod.RoomBoundarySubface);
    }
}
