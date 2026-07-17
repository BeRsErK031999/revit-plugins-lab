using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class RoomScopeServiceTests
{
    private static readonly AxisAlignedBox3D Bounds = new(0, 0, 0, 10, 10, 3);

    [Fact]
    public void Select_EntireProjectSeparatesInvalidRooms()
    {
        FinishRoomScopeResult result = new RoomScopeService().Select(
        [
            Room(4, 1),
            Room(1, 1),
            Room(2, 1, area: 0),
            Room(3, 1, hasLocation: false),
            Room(5, 1, hasBounds: false)
        ],
            ReportScopeSettings.EntireProject());

        Assert.Equal([1L, 4L], result.SelectedRooms.Select(room => room.ElementId));
        Assert.Equal(3, result.InvalidRooms.Count);
        Assert.Contains(result.InvalidRooms, room => room.Reason == FinishRoomSkipReason.Unplaced);
        Assert.Contains(result.InvalidRooms, room => room.Reason == FinishRoomSkipReason.NotEnclosed);
        Assert.Contains(result.InvalidRooms, room => room.Reason == FinishRoomSkipReason.MissingBounds);
    }

    [Fact]
    public void Select_LevelScopeUsesRoomLevelWithoutAdditionalCollection()
    {
        FinishRoomScopeResult result = new RoomScopeService().Select(
            [Room(1, 10), Room(2, 20), Room(3, 10)],
            new ReportScopeSettings(ReportScopeKind.Level, 10, null, string.Empty));

        Assert.Equal([1L, 3L], result.SelectedRooms.Select(room => room.ElementId));
        Assert.Equal(1, result.OutsideScopeCount);
    }

    [Fact]
    public void Select_SectionScopeMatchesRawOrDisplayAndReportsMissingValue()
    {
        ParameterReference section = ParameterReference.Project(
            "Секция",
            100,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
        Dictionary<string, FinishParameterValueSnapshot> raw = new()
        {
            [section.StableKey] = new FinishParameterValueSnapshot("A", "Секция A")
        };
        Dictionary<string, FinishParameterValueSnapshot> display = new()
        {
            [section.StableKey] = new FinishParameterValueSnapshot("1", "A")
        };

        FinishRoomScopeResult result = new RoomScopeService().Select(
            [Room(1, 10, values: raw), Room(2, 10, values: display), Room(3, 10)],
            new ReportScopeSettings(ReportScopeKind.Section, null, section, "a"));

        Assert.Equal([1L, 2L], result.SelectedRooms.Select(room => room.ElementId));
        Assert.Equal(1, result.MissingScopeValueCount);
        Assert.Equal(1, result.OutsideScopeCount);
    }

    private static FinishRoomCandidateSnapshot Room(
        long id,
        long levelId,
        double area = 10,
        bool hasLocation = true,
        bool hasBounds = true,
        AxisAlignedBox3D? bounds = null,
        IReadOnlyDictionary<string, FinishParameterValueSnapshot>? values = null)
    {
        return new FinishRoomCandidateSnapshot(
            id,
            levelId,
            area,
            hasLocation,
            bounds ?? (area > 0 && hasLocation && hasBounds ? Bounds : null),
            values);
    }
}
