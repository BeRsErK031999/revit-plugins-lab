using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishOccurrenceAccumulatorTests
{
    [Fact]
    public void Build_SumsRepeatedSubfacesWithoutIntermediateRounding()
    {
        FinishOccurrenceAccumulator accumulator = new();
        accumulator.Add(1, 101, FinishPreviewCategory.Walls, 1.23456, FinishQuantityMethod.RoomBoundarySubface);
        accumulator.Add(1, 101, FinishPreviewCategory.Walls, 2.34567, FinishQuantityMethod.RoomBoundarySubface);

        FinishOccurrence occurrence = Assert.Single(accumulator.Build());

        Assert.Equal(3.58023, occurrence.AreaSquareMeters, precision: 5);
    }

    [Fact]
    public void Build_KeepsOneSlabIndependentForEveryRoom()
    {
        FinishOccurrenceAccumulator accumulator = new();
        accumulator.Add(1, 200, FinishPreviewCategory.Floors, 12, FinishQuantityMethod.FloorProbeIntersection);
        accumulator.Add(2, 200, FinishPreviewCategory.Floors, 18, FinishQuantityMethod.FloorProbeIntersection);

        FinishQuantityResult result = new(accumulator.Build(), []);

        Assert.Equal(2, result.Occurrences.Count);
        Assert.Equal(2, result.Summary.Floors.RoomCount);
        Assert.Equal(1, result.Summary.Floors.ElementCount);
        Assert.Equal(30, result.Summary.Floors.AreaSquareMeters);
    }

    [Fact]
    public void Build_UsesActualSlopedCeilingBoundaryArea()
    {
        FinishOccurrenceAccumulator accumulator = new();
        accumulator.Add(
            1,
            200,
            FinishPreviewCategory.Ceilings,
            4.368,
            FinishQuantityMethod.RoomBoundarySubface);

        FinishOccurrence occurrence = Assert.Single(accumulator.Build());

        Assert.Equal(FinishPreviewCategory.Ceilings, occurrence.Category);
        Assert.Equal(FinishQuantityMethod.RoomBoundarySubface, occurrence.Method);
        Assert.Equal(4.368, occurrence.AreaSquareMeters, precision: 3);
    }

    [Fact]
    public void Add_RejectsMixedMethodsForSameRoomElementCategory()
    {
        FinishOccurrenceAccumulator accumulator = new();
        accumulator.Add(1, 101, FinishPreviewCategory.Walls, 3, FinishQuantityMethod.RoomBoundarySubface);

        Assert.Throws<InvalidOperationException>(() => accumulator.Add(
            1,
            101,
            FinishPreviewCategory.Walls,
            2,
            FinishQuantityMethod.WallProbeIntersection));
    }
}
