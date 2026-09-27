using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.ScheduleColumnCollapse.Services;

public sealed class ScheduleColumnVisibilityAnalyzerTests
{
    private readonly ScheduleColumnVisibilityAnalyzer analyzer = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyzeColumn_HidesZeroNumericColumnRegardlessOfInitialVisibility(bool isHidden)
    {
        ScheduleColumnState column = NumericColumn(isHidden, ["ф12", "0.0", "0,0", "0", "−0,0"]);

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData(false, "62,9")]
    [InlineData(true, "62,9")]
    [InlineData(false, "−0,1")]
    [InlineData(true, "1\u202f234,5")]
    public void AnalyzeColumn_ShowsNonZeroNumericColumnRegardlessOfInitialVisibility(bool isHidden, string value)
    {
        ScheduleColumnState column = NumericColumn(isHidden, ["ф12", "0.0", value, "0.0"]);

        Assert.Equal(ScheduleColumnVisibilityAction.Show, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyzeColumn_HidesEmptyNumericColumnIncludingSuppressedZeros(bool isHidden)
    {
        ScheduleColumnState column = NumericColumn(isHidden, ["ф12", "", " ", "\u00a0", "—", "–", "-"]);

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, analyzer.AnalyzeColumn(column).Action);
    }

    [Fact]
    public void AnalyzeColumn_HidesNumericColumnWithOnlyAHeading()
    {
        ScheduleColumnState column = NumericColumn(true, ["ф12"]);

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData("Итого A400", "Итого")]
    [InlineData("Прокат марки C255 -10", "-10")]
    public void AnalyzeColumn_DoesNotCountHeadingAsANonZeroValue(string fieldName, string heading)
    {
        ScheduleColumnState column = NumericColumn(true, [heading, "0.0", "0.0"]) with
        {
            FieldName = fieldName,
            ColumnHeading = heading
        };

        Assert.Equal(ScheduleColumnVisibilityAction.Hide, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData(false, "Перекрытие на отметке +2,900")]
    [InlineData(true, "Перекрытие на отметке +2,900")]
    [InlineData(true, "")]
    [InlineData(true, "12")]
    [InlineData(false, "0")]
    public void AnalyzeColumn_PreservesTextAndServiceFieldVisibility(bool isHidden, string value)
    {
        ScheduleColumnState column = new(
            FieldName: "Марка элемента",
            ColumnHeading: "Марка элемента",
            IsHidden: isHidden,
            CanHide: true,
            CellTexts: [value],
            IsNumeric: false);

        Assert.Equal(ScheduleColumnVisibilityAction.Keep, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData(false, "0")]
    [InlineData(true, "62,9")]
    public void AnalyzeColumn_PreservesProtectedNumericFieldVisibility(bool isHidden, string value)
    {
        ScheduleColumnState column = NumericColumn(isHidden, [value]) with { CanHide = false };

        Assert.Equal(ScheduleColumnVisibilityAction.Keep, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnalyzeColumn_PreservesOriginalVisibilityWhenNumericValuesCannotBeRead(bool isHidden)
    {
        ScheduleColumnState column = NumericColumn(isHidden, ["ф12", "0,0", "<Различные>"]);

        Assert.Equal(ScheduleColumnVisibilityAction.Keep, analyzer.AnalyzeColumn(column).Action);
    }

    [Theory]
    [InlineData(0.0, ScheduleColumnVisibilityAction.Hide)]
    [InlineData(0.5, ScheduleColumnVisibilityAction.Show)]
    public void AnalyzeColumn_UsesRevitParsedValuesForCellsWithUnits(double value, ScheduleColumnVisibilityAction expected)
    {
        ScheduleColumnState column = NumericColumn(true, ["ф12", "0,0 кг", $"{value} кг"]) with
        {
            ParsedNumericValues = [null, 0.0, value]
        };

        Assert.Equal(expected, analyzer.AnalyzeColumn(column).Action);
    }

    [Fact]
    public void AnalyzeColumn_ShowsConfirmedNonZeroValueEvenWhenAnotherRowIsUnparsed()
    {
        ScheduleColumnState column = NumericColumn(true, ["<Различные>", "0", "62,9"]);

        Assert.Equal(ScheduleColumnVisibilityAction.Show, analyzer.AnalyzeColumn(column).Action);
    }

    private static ScheduleColumnState NumericColumn(bool isHidden, IReadOnlyList<string> cellTexts)
    {
        return new ScheduleColumnState(
            FieldName: "Р - ИА • A240 ф12",
            ColumnHeading: "ф12",
            IsHidden: isHidden,
            CanHide: true,
            CellTexts: cellTexts,
            IsNumeric: true);
    }
}
