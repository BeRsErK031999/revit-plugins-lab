using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishUnassignedElementWarningBuilderTests
{
    [Fact]
    public void ClassifiedFloorOutsideEverySearchStillReportsIncompleteArea()
    {
        FinishClassifiedElement floor = Floor(2700621);
        IReadOnlyList<FinishGeometryWarning> warnings = new FinishUnassignedElementWarningBuilder().Build(
            [floor], [], new HashSet<long>());

        FinishGeometryWarning warning = Assert.Single(warnings);
        Assert.Equal(2700621L, warning.ElementId);
        Assert.Equal(FinishGeometryWarningCode.UnassignedElement, warning.Code);
        Assert.Contains("не попал в область поиска", warning.Message);
        Assert.True(FinishGeometryWarningClassifier.AffectsScheduleValue(warning));
    }

    [Fact]
    public void AssignedElementsAreExcludedAndUnresolvedCandidatesAreReportedOnce()
    {
        FinishClassifiedElement unresolved = Floor(101);
        FinishClassifiedElement assigned = Floor(102);
        FinishOccurrence contact = new(1, 102, FinishPreviewCategory.Floors, 20, FinishQuantityMethod.ProjectedRoomFootprint);
        IReadOnlyList<FinishGeometryWarning> warnings = new FinishUnassignedElementWarningBuilder().Build(
            [unresolved, unresolved, assigned], [contact], new HashSet<long> { 101, 102 });

        FinishGeometryWarning warning = Assert.Single(warnings);
        Assert.Equal(101L, warning.ElementId);
        Assert.Contains("контакт с помещением не найден", warning.Message);
    }

    private static FinishClassifiedElement Floor(long id) =>
        new(new FinishElementCandidateSnapshot(id, 100, FinishPhysicalCategory.Floor, null), FinishPreviewCategory.Floors);
}
