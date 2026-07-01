using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.ScheduleColumnCollapse.Services;

public sealed class ScheduleColumnVisibilityAnalyzerTests
{
    private readonly ScheduleColumnVisibilityAnalyzer analyzer = new();

    [Fact]
    public void AnalyzeColumn_HidesNumericColumnWhenAllValuesAreZero()
    {
        ScheduleColumnVisibilityDecision decision = analyzer.AnalyzeColumn(new ScheduleColumnState(
            FieldName: "Р - ИА • Вр-I ф3",
            ColumnHeading: "ф3",
            IsHidden: false,
            CanHide: true,
            CellTexts: ["ф3", "0.0", "0,0", "0"]));

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, decision.Action);
    }

    [Fact]
    public void AnalyzeColumn_ShowsNumericColumnWhenAnyValueIsNotZero()
    {
        ScheduleColumnVisibilityDecision decision = analyzer.AnalyzeColumn(new ScheduleColumnState(
            FieldName: "Р - ИА • A240 ф12",
            ColumnHeading: "ф12",
            IsHidden: false,
            CanHide: true,
            CellTexts: ["ф12", "0.0", "62,9", "0.0"]));

        Assert.Equal(ScheduleColumnVisibilityAction.Show, decision.Action);
    }

    [Fact]
    public void AnalyzeColumn_HidesTotalColumnWhenAllValuesAreZero()
    {
        ScheduleColumnVisibilityDecision decision = analyzer.AnalyzeColumn(new ScheduleColumnState(
            FieldName: "Итого A400",
            ColumnHeading: "Итого",
            IsHidden: false,
            CanHide: true,
            CellTexts: ["Итого", "0.0", "0.0"]));

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, decision.Action);
    }

    [Fact]
    public void AnalyzeColumn_HidesZeroColumnWhenNumericHeadingAppearsInCells()
    {
        ScheduleColumnVisibilityDecision decision = analyzer.AnalyzeColumn(new ScheduleColumnState(
            FieldName: "Прокат марки C255 -10",
            ColumnHeading: "-10",
            IsHidden: false,
            CanHide: true,
            CellTexts: ["-10", "0.0", "0.0"]));

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, decision.Action);
    }

    [Fact]
    public void AnalyzeColumn_LeavesTextColumnsVisible()
    {
        ScheduleColumnVisibilityDecision decision = analyzer.AnalyzeColumn(new ScheduleColumnState(
            FieldName: "Марка элемента",
            ColumnHeading: "Марка элемента",
            IsHidden: false,
            CanHide: true,
            CellTexts: ["Вертикальные конструкции", "Перекрытие на отметке +2,900"]));

        Assert.Equal(ScheduleColumnVisibilityAction.Show, decision.Action);
    }
}
