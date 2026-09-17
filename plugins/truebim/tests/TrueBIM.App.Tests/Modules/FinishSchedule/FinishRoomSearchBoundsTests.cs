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

    [Theory]
    // The basement finish is 1150 mm above its Room base; the platform is 550 mm below it.
    [InlineData(-36.90944881889764, -33.33333333333333, -33.13648293963254)]
    [InlineData(0, -2.7559055118110236, -1.804461942257218)]
    public void FloorOnRoomLevelIsFoundAtItsActualOffset(double roomBase, double floorBottom, double floorTop)
    {
        FinishRoomCandidateSnapshot room = Room(1, 10, roomBase, roomBase + 8);
        FinishClassifiedElement floor = Floor(2700621, 10, new AxisAlignedBox3D(1, 1, floorBottom, 9, 9, floorTop));
        FinishRoomSearchBounds search = new([room], [floor]);

        Assert.True(search.Includes(room, floor));
        Assert.True(search.Create(room, FinishPreviewCategory.Floors).Intersects(floor.Element.Bounds!));
    }

    [Fact]
    public void RaisedFloorDoesNotBecomeCandidateOfAnotherStoreyWithSamePlan()
    {
        FinishRoomCandidateSnapshot downstairs = Room(1, 10, 0, 8);
        FinishRoomCandidateSnapshot upstairs = Room(2, 20, 14, 22);
        FinishClassifiedElement raised = Floor(101, 10, new AxisAlignedBox3D(1, 1, 3.5, 9, 9, 3.8));
        FinishClassifiedElement upperFloor = Floor(102, 20, new AxisAlignedBox3D(1, 1, 13.5, 9, 9, 14));
        FinishRoomSearchBounds search = new([downstairs, upstairs], [raised, upperFloor]);

        Assert.True(search.Includes(downstairs, raised));
        Assert.False(search.Includes(upstairs, raised));
        Assert.True(search.Includes(upstairs, upperFloor));
        Assert.False(search.Includes(downstairs, upperFloor));
    }

    [Fact]
    public void DeepRecessOnSameLevelDoesNotAllowFloorOfLowerLevel()
    {
        FinishRoomCandidateSnapshot lower = Room(1, 10, 0, 8);
        FinishRoomCandidateSnapshot upper = Room(2, 20, 14, 22);
        FinishClassifiedElement recess = Floor(101, 20, new AxisAlignedBox3D(1, 1, -2, 9, 9, -1.8));
        FinishClassifiedElement lowerFloor = Floor(102, 10, new AxisAlignedBox3D(1, 1, -0.5, 9, 9, 0));
        FinishRoomSearchBounds search = new([lower, upper], [recess, lowerFloor]);

        Assert.True(search.Includes(upper, recess));
        Assert.True(search.Create(upper, FinishPreviewCategory.Floors).Intersects(lowerFloor.Element.Bounds!));
        Assert.False(search.Includes(upper, lowerFloor));
    }

    [Fact]
    public void OffsetFloorInDifferentWingDoesNotExpandRoomSearch()
    {
        FinishRoomCandidateSnapshot room = Room(1, 10, 0, 8);
        FinishClassifiedElement remote = Floor(101, 10, new AxisAlignedBox3D(20, 1, 15, 30, 9, 16));
        FinishRoomSearchBounds search = new([room], [remote]);

        Assert.False(search.Includes(room, remote));
        Assert.False(search.Create(room, FinishPreviewCategory.Floors).Intersects(new AxisAlignedBox3D(1, 1, 15, 9, 9, 16)));
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

    private static FinishClassifiedElement Floor(long id, long levelId, AxisAlignedBox3D bounds) =>
        new(new FinishElementCandidateSnapshot(id, 100, FinishPhysicalCategory.Floor, bounds) { LevelId = levelId },
            FinishPreviewCategory.Floors);
}
