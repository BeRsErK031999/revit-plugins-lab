using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class StoredFinishQuantityCalculatorTests
{
    [Theory]
    [InlineData(FinishPreviewCategory.Walls)]
    [InlineData(FinishPreviewCategory.Floors)]
    [InlineData(FinishPreviewCategory.Ceilings)]
    public void UndefinedMarkerExcludesEvenStaleRoomOrFloorAssignments(FinishPreviewCategory category)
    {
        var rooms = new[] { Room(1, "210") };
        var oldShare = new FinishOccurrence(1, 100, category, 2.5, FinishQuantityMethod.FloorRoomSurface);
        var stored = new StoredFinishElement(100, category, 10, "[Не определено]", 12.75, 1,
            category == FinishPreviewCategory.Floors ? [oldShare] : null);
        var calculator = new StoredFinishQuantityCalculator();
        var marked = calculator.Calculate([stored], rooms, RoomIdentifierSettings.ByNumber());
        Assert.Empty(marked.Occurrences);
        Assert.Equal(FinishOwnershipFinding.MarkedUnassigned, Assert.Single(marked.Warnings).OwnershipFinding);
        var assigned = calculator.Calculate([stored with { Ownership = "210", AssignedRoomId = null, FloorAreas = null }],
            rooms, RoomIdentifierSettings.ByNumber());
        Assert.Equal(12.75, Assert.Single(assigned.Occurrences).AreaSquareMeters);
    }

    [Theory]
    [InlineData(FinishPreviewCategory.Walls)]
    [InlineData(FinishPreviewCategory.Floors)]
    [InlineData(FinishPreviewCategory.Ceilings)]
    public void ManualReassignmentMovesWholeAreaToChosenRoom(FinishPreviewCategory category)
    {
        FinishRoomCandidateSnapshot[] rooms = [Room(1, "210"), Room(2, "213")];
        StoredFinishElement element = new(100, category, 10, "210", 12.75);
        StoredFinishQuantityCalculator calculator = new();
        FinishQuantityResult before = calculator.Calculate([element], rooms, RoomIdentifierSettings.ByNumber());
        FinishQuantityResult after = calculator.Calculate(
            [element with { Ownership = " 213 " }], rooms, RoomIdentifierSettings.ByNumber());
        Assert.Equal(1, Assert.Single(before.Occurrences).RoomId);
        FinishOccurrence occurrence = Assert.Single(after.Occurrences);
        Assert.Equal(2, occurrence.RoomId);
        Assert.Equal(12.75, occurrence.AreaSquareMeters);
        Assert.Equal(category, occurrence.Category);
        Assert.Equal(FinishQuantityMethod.StoredOwnership, occurrence.Method);
        Assert.Equal(FinishGeometryCacheMetrics.Empty, after.CacheMetrics);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("999")]
    [InlineData("210, 213")]
    public void InvalidOwnershipNeverInventsOrSplitsArea(string? ownership)
    {
        FinishQuantityResult result = Calculate(ownership, [Room(1, "210"), Room(2, "213")]);
        Assert.Empty(result.Occurrences);
        Assert.Equal(FinishGeometryWarningCode.StoredOwnershipUnresolved, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public void AutomaticFloorSharesSurviveDuplicateRoomNumbersAndExcludeOutsideArea()
    {
        var parts = new[] {
            new FinishOccurrence(1, 100, FinishPreviewCategory.Floors, 6, FinishQuantityMethod.FloorRoomSurface),
            new FinishOccurrence(2, 100, FinishPreviewCategory.Floors, 2, FinishQuantityMethod.FloorRoomSurface)
        };
        var element = new StoredFinishElement(100, FinishPreviewCategory.Floors, 10, "101", 12, FloorAreas: parts);
        var calculation = new StoredFinishQuantityCalculator();
        var result = calculation.Calculate([element], [Room(1, "101"), Room(2, "101")], RoomIdentifierSettings.ByNumber());
        Assert.Equal(8, result.Occurrences.Sum(o => o.AreaSquareMeters));
        Assert.Equal(2, result.Occurrences.Count);
        var changed = calculation.Calculate([element], [Room(1, "101"), Room(2, "102")], RoomIdentifierSettings.ByNumber());
        Assert.Empty(changed.Occurrences);
        Assert.Single(changed.Warnings);
    }

    [Fact]
    public void MeasuredFloorOutsideRoomsIsQuietAndZero()
    {
        var element = new StoredFinishElement(100, FinishPreviewCategory.Floors, 10, "", 12, FloorAreas: []);
        var result = new StoredFinishQuantityCalculator().Calculate([element], [Room(1, "101")], RoomIdentifierSettings.ByNumber());
        Assert.Empty(result.Occurrences);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RepeatedNumberUsesUniqueRoomOnElementLevel()
    {
        FinishQuantityResult result = Calculate("101", [Room(1, "101", 20), Room(2, "101", 10)]);
        Assert.Equal(2, Assert.Single(result.Occurrences).RoomId);
    }

    [Fact]
    public void RepeatedNumberOnSameLevelCountsFullAreaOnce()
    {
        FinishQuantityResult result = Calculate("101", [Room(1, "101"), Room(2, "101")]);
        Assert.Equal(5, Assert.Single(result.Occurrences).AreaSquareMeters);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(2, "101", 2)]
    [InlineData(999, "101", 1)]
    [InlineData(2, "102", 3)]
    public void StoredPhysicalRoomIsUsedOnlyWhileItMatchesTheText(long assignedRoomId, string ownership, long expectedRoomId)
    {
        FinishQuantityResult result = new StoredFinishQuantityCalculator().Calculate(
            [new StoredFinishElement(100, FinishPreviewCategory.Walls, 10, ownership, 5, assignedRoomId)],
            [Room(1, "101"), Room(2, "101"), Room(3, "102")], RoomIdentifierSettings.ByNumber());
        Assert.Equal(expectedRoomId, Assert.Single(result.Occurrences).RoomId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void RepeatedNamesAndRepeatedInputElementsDoNotMultiplyArea()
    {
        StoredFinishElement element = new(100, FinishPreviewCategory.Walls, 10, "  ПОМЕЩЕНИЕ  ", 5);
        FinishQuantityResult result = new StoredFinishQuantityCalculator().Calculate([element, element],
            [new FinishRoomCandidateSnapshot(2, 10, 20, true, null, name: "Помещение"),
             new FinishRoomCandidateSnapshot(1, 10, 20, true, null, name: "Помещение")],
            new RoomIdentifierSettings(RoomIdentifierMode.Name, null));
        Assert.Equal(1, Assert.Single(result.Occurrences).RoomId);
        Assert.Equal(5, Assert.Single(result.Occurrences).AreaSquareMeters);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void UniqueOwnershipIsAuthoritativeEvenOnAnotherLevel()
    {
        FinishQuantityResult result = Calculate("101", [Room(1, "101", 20)]);
        Assert.Equal(1, Assert.Single(result.Occurrences).RoomId);
    }

    [Fact]
    public void NameIdentifierIsMatchedAsWholeValueIncludingComma()
    {
        FinishQuantityResult result = new StoredFinishQuantityCalculator().Calculate(
            [new StoredFinishElement(100, FinishPreviewCategory.Walls, 10, "Холл, секция А", 5)],
            [new FinishRoomCandidateSnapshot(1, 10, 20, true, null, name: "Холл, секция А")],
            new RoomIdentifierSettings(RoomIdentifierMode.Name, null));
        Assert.Equal(1, Assert.Single(result.Occurrences).RoomId);
    }

    private static FinishQuantityResult Calculate(string? ownership, FinishRoomCandidateSnapshot[] rooms) =>
        new StoredFinishQuantityCalculator().Calculate(
            [new StoredFinishElement(100, FinishPreviewCategory.Walls, 10, ownership, 5)],
            rooms, RoomIdentifierSettings.ByNumber());

    private static FinishRoomCandidateSnapshot Room(long id, string number, long level = 10) =>
        new(id, level, 20, true, null, number: number);
}
