using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishRoomSearchBoundsTests
{
    [Theory]
    [InlineData(FinishPreviewCategory.Walls)]
    [InlineData(FinishPreviewCategory.Ceilings)]
    public void SearchReachesHighFinishRegardlessOfRoomHeightAndStopsAtNextStorey(FinishPreviewCategory category)
    {
        FinishRoomCandidateSnapshot shortRoom = Room(1, 1, 0, 8);
        FinishRoomCandidateSnapshot tallRoom = Room(1, 1, 0, 20);
        FinishRoomCandidateSnapshot upstairs = Room(2, 2, 22, 30);
        AxisAlignedBox3D lowSearch = new FinishRoomSearchBounds([shortRoom, upstairs]).Create(shortRoom, category);
        AxisAlignedBox3D highSearch = new FinishRoomSearchBounds([tallRoom, upstairs]).Create(tallRoom, category);
        Assert.Equal(lowSearch, highSearch);
        Assert.True(lowSearch.Intersects(new AxisAlignedBox3D(1, 1, 19, 2, 2, 20)));
        Assert.False(lowSearch.Intersects(new AxisAlignedBox3D(1, 1, 25, 2, 2, 26)));
    }

    [Fact]
    public void HighestStoreyHasNoArbitraryTwoMeterCeilingCutoff()
    {
        FinishRoomCandidateSnapshot room = Room(1, 1, 0, 8);
        AxisAlignedBox3D search = new FinishRoomSearchBounds([room]).Create(room, FinishPreviewCategory.Ceilings);
        Assert.True(search.Intersects(new AxisAlignedBox3D(1, 1, 30, 2, 2, 31)));
    }

    [Fact]
    public void IntermediateStoreyInAnotherWingDoesNotCutOffHighCeiling()
    {
        FinishRoomCandidateSnapshot room = Room(1, 1, 0, 8);
        FinishRoomCandidateSnapshot remote = new(2, 2, 100, true, new AxisAlignedBox3D(20, 0, 20, 30, 10, 28));
        AxisAlignedBox3D search = new FinishRoomSearchBounds([room, remote]).Create(room, FinishPreviewCategory.Ceilings);
        Assert.True(search.Intersects(new AxisAlignedBox3D(1, 1, 42, 9, 9, 43)));
    }

    [Fact]
    public void SearchStopsAtOverlappingRoomAboveInsteadOfLowerRemoteRoom()
    {
        FinishRoomCandidateSnapshot room = Room(1, 1, 0, 8);
        FinishRoomCandidateSnapshot remote = new(2, 2, 100, true, new AxisAlignedBox3D(10, 0, 20, 20, 10, 28));
        FinishRoomCandidateSnapshot above = Room(3, 3, 40, 48);
        AxisAlignedBox3D search = new FinishRoomSearchBounds([room, remote, above]).Create(room, FinishPreviewCategory.Ceilings);
        Assert.True(search.Intersects(new AxisAlignedBox3D(1, 1, 30, 9, 9, 31)));
        Assert.False(search.Intersects(new AxisAlignedBox3D(1, 1, 42, 9, 9, 43)));
    }

    [Fact]
    public void FloorSearchIncludesSmallOffsetBelowRoomBase()
    {
        FinishRoomCandidateSnapshot room = Room(1, 1, 0, 8);
        AxisAlignedBox3D search = new FinishRoomSearchBounds([room]).Create(room, FinishPreviewCategory.Floors);
        Assert.True(search.Intersects(new AxisAlignedBox3D(1, 1, -0.5, 2, 2, -0.2)));
        Assert.False(search.Intersects(new AxisAlignedBox3D(1, 1, -12, 2, 2, -11)));
    }

    [Fact]
    public void AlternateContactMeasurementDoesNotSumDuplicateGeometry()
    {
        FinishOccurrenceAccumulator accumulator = new();
        accumulator.Add(1, 10, FinishPreviewCategory.Walls, 3, FinishQuantityMethod.RoomBoundarySubface);
        accumulator.KeepLargest(new FinishOccurrence(1, 10, FinishPreviewCategory.Walls, 5, FinishQuantityMethod.ProjectedRoomFootprint));
        accumulator.KeepLargest(new FinishOccurrence(1, 10, FinishPreviewCategory.Walls, 4, FinishQuantityMethod.ProjectedRoomFootprint));
        Assert.Equal(5, Assert.Single(accumulator.Build()).AreaSquareMeters);
    }

    private static FinishRoomCandidateSnapshot Room(long id, long levelId, double bottom, double top) =>
        new(id, levelId, 100, true, new AxisAlignedBox3D(0, 0, bottom, 10, 10, top));
}
