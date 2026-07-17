using TrueBIM.App.Modules.FinishSchedule.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishQuantityModelsTests
{
    [Fact]
    public void QuantityRequest_IsDeterministicRegardlessOfInputOrder()
    {
        FinishQuantityRequest request = new(
        [
            Room(3),
            Room(1),
            Room(2),
            Room(1)
        ],
        [
            Classified(20, FinishPreviewCategory.Floors),
            Classified(10, FinishPreviewCategory.Walls),
            Classified(10, FinishPreviewCategory.Walls)
        ]);

        Assert.Equal([1L, 2L, 3L], request.Rooms.Select(room => room.ElementId));
        Assert.Equal([10L, 20L], request.Elements.Select(element => element.Element.ElementId));
    }

    [Fact]
    public void PreviewResult_WithQuantitiesPreservesSummaryAndGeometryWarnings()
    {
        FinishSchedulePreviewResult preview = new(
            1,
            new FinishRoomScopeResult([Room(1)], [], 0, 0),
            new FinishPreviewCategoryCounts(1, 1, 1),
            new FinishPreviewCategoryCounts(0, 0, 0),
            new FinishPreviewCategoryCounts(0, 0, 0),
            new FinishPreviewIndexCounts(1, 0, 1),
            ["Предупреждение scope."]);
        FinishQuantityResult quantities = new(
        [
            new FinishOccurrence(
                1,
                10,
                FinishPreviewCategory.Walls,
                14.25,
                FinishQuantityMethod.RoomBoundarySubface)
        ],
        [
            new FinishGeometryWarning(
                FinishGeometryWarningCode.WallFallbackUnresolved,
                "Неопределённая стена.",
                RoomId: 1,
                ElementId: 11,
                Category: FinishPreviewCategory.Walls)
        ]);

        FinishSchedulePreviewResult result = preview.WithQuantities(quantities);

        Assert.NotNull(result.Quantities);
        Assert.Equal(14.25, result.Quantities.Walls.AreaSquareMeters);
        Assert.Equal(["Предупреждение scope.", "Неопределённая стена."], result.Warnings);
    }

    private static FinishRoomCandidateSnapshot Room(long id)
    {
        return new FinishRoomCandidateSnapshot(
            id,
            1,
            20,
            true,
            new AxisAlignedBox3D(0, 0, 0, 10, 10, 3));
    }

    private static FinishClassifiedElement Classified(long id, FinishPreviewCategory category)
    {
        FinishPhysicalCategory physicalCategory = category == FinishPreviewCategory.Walls
            ? FinishPhysicalCategory.Wall
            : FinishPhysicalCategory.Floor;
        return new FinishClassifiedElement(
            new FinishElementCandidateSnapshot(
                id,
                id + 100,
                physicalCategory,
                new AxisAlignedBox3D(0, 0, 0, 10, 10, 3)),
            category);
    }
}
