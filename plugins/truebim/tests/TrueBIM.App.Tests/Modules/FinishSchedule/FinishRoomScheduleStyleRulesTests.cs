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
}
