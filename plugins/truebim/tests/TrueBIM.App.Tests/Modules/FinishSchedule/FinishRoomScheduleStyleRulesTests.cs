using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishRoomScheduleStyleRulesTests
{
    [Fact]
    public void HeaderDimensions_MatchFinishScheduleRequirement()
    {
        Assert.Equal(12, FinishRoomScheduleStyleRules.TitleRowHeightMillimeters);
        Assert.Equal(3.5, FinishRoomScheduleStyleRules.TitleTextSizeMillimeters);
        Assert.Equal(2.5, FinishRoomScheduleStyleRules.ColumnHeaderTextSizeMillimeters);
        Assert.False(FinishRoomScheduleStyleRules.ShowBlankLineBetweenGroups);
    }

    [Fact]
    public void BodyBorders_UseNormalFrameAndVerticalsWithThinIntermediateHorizontals()
    {
        FinishScheduleCellBorderRules first = FinishRoomScheduleStyleRules.BodyBorders(
            isFirstRow: true,
            isLastRow: false);
        FinishScheduleCellBorderRules middle = FinishRoomScheduleStyleRules.BodyBorders(
            isFirstRow: false,
            isLastRow: false);
        FinishScheduleCellBorderRules last = FinishRoomScheduleStyleRules.BodyBorders(
            isFirstRow: false,
            isLastRow: true);

        Assert.Equal(FinishScheduleLineWeight.Normal, first.Top);
        Assert.Equal(FinishScheduleLineWeight.Thin, middle.Top);
        Assert.Equal(FinishScheduleLineWeight.Thin, middle.Bottom);
        Assert.Equal(FinishScheduleLineWeight.Normal, last.Bottom);
        Assert.All(
            new[] { first, middle, last },
            borders =>
            {
                Assert.Equal(FinishScheduleLineWeight.Normal, borders.Left);
                Assert.Equal(FinishScheduleLineWeight.Normal, borders.Right);
            });
    }

    [Fact]
    public void HeaderBorders_AreNormalOnEverySide()
    {
        FinishScheduleCellBorderRules borders = FinishRoomScheduleStyleRules.HeaderBorders;

        Assert.All(
            new[] { borders.Top, borders.Bottom, borders.Left, borders.Right },
            weight => Assert.Equal(FinishScheduleLineWeight.Normal, weight));
    }

    [Fact]
    public void BuildHeaderCells_PutsGraphLettersAndNoteInsideThreeLevelHeader()
    {
        FinishRoomSchedulePlan plan = new FinishRoomSchedulePlanBuilder().Build(
            Settings(),
            [Room()]);

        IReadOnlyList<FinishScheduleHeaderCell> cells =
            FinishRoomScheduleStyleRules.BuildHeaderCells(plan.Columns);

        Assert.Contains(cells, cell =>
            cell == new FinishScheduleHeaderCell(
                0,
                0,
                1,
                0,
                FinishRoomScheduleStyleRules.RoomHeaderText));
        Assert.Contains(cells, cell =>
            cell == new FinishScheduleHeaderCell(
                0,
                1,
                0,
                6,
                FinishRoomScheduleStyleRules.FinishGroupHeaderText));
        Assert.Contains(cells, cell =>
            cell == new FinishScheduleHeaderCell(
                0,
                7,
                1,
                7,
                FinishRoomScheduleStyleRules.NoteHeaderText));
        Assert.Equal(
            ["A", "B", "C", "D", "E", "F", "G", "H"],
            cells
                .Where(cell => cell.TopRowOffset == 2)
                .OrderBy(cell => cell.LeftColumnIndex)
                .Select(cell => cell.Text));
    }

    [Theory]
    [InlineData("Обычные линии", "Обычные линии")]
    [InlineData("<Обычные линии>", "Обычные линии")]
    [InlineData(" Тонкие линии ", "<Тонкие линии>")]
    public void MatchesLineStyleName_AcceptsRevitDisplayDecorators(
        string actual,
        string expected)
    {
        Assert.True(FinishRoomScheduleStyleRules.MatchesLineStyleName(actual, expected));
    }

    private static FinishScheduleSettings Settings()
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        return defaults with
        {
            RoomListOutputParameter = Reference("Room list", 1),
            Walls = defaults.Walls with
            {
                OutputDescriptionParameter = Reference("Wall description", 2),
                OutputAreaParameter = Reference("Wall area", 3)
            },
            Ceilings = defaults.Ceilings with
            {
                OutputDescriptionParameter = Reference("Ceiling description", 4),
                OutputAreaParameter = Reference("Ceiling area", 5)
            },
            Floors = defaults.Floors with
            {
                OutputDescriptionParameter = Reference("Floor description", 6),
                OutputAreaParameter = Reference("Floor area", 7)
            }
        };
    }

    private static ParameterReference Reference(string name, long id)
    {
        return ParameterReference.Project(
            name,
            id,
            ParameterBindingKind.Instance,
            ParameterStorageKind.String);
    }

    private static FinishRoomCandidateSnapshot Room()
    {
        return new FinishRoomCandidateSnapshot(
            100,
            10,
            20,
            true,
            new AxisAlignedBox3D(0, 0, 0, 1, 1, 1));
    }
}
