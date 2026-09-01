using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleRegister;

public sealed class ScheduleRegisterAggregationServiceTests
{
    private readonly ScheduleRegisterAggregationService service = new();

    [Fact]
    public void Aggregate_CombinesDifferentSegmentsOfOneScheduleAcrossSheets()
    {
        ScheduleRegisterAggregationResult result = service.Aggregate(
        [
            Placement(10, "Спецификация окон", "Спецификация окон", 101, "24", 0),
            Placement(10, "Спецификация окон", "Спецификация окон", 102, "25", 1)
        ]);

        ScheduleRegisterRow row = Assert.Single(result.Rows);
        Assert.Equal("24, 25", row.SheetNumbers);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Aggregate_WarnsWhenSameSegmentIsPlacedOnDifferentSheets()
    {
        ScheduleRegisterAggregationResult result = service.Aggregate(
        [
            Placement(10, "Спецификация окон", "Спецификация окон", 101, "24", 0),
            Placement(10, "Спецификация окон", "Спецификация окон", 102, "25", 0)
        ]);

        Assert.Single(result.Rows);
        Assert.Contains(result.Warnings, warning => warning.Contains("размещена повторно", StringComparison.CurrentCultureIgnoreCase));
    }

    [Fact]
    public void Aggregate_KeepsDifferentSchedulesWithSameTitleAndWarns()
    {
        ScheduleRegisterAggregationResult result = service.Aggregate(
        [
            Placement(10, "Окна, вариант 1", "Спецификация окон", 101, "8", 0),
            Placement(11, "Окна, вариант 2", "Спецификация окон", 102, "9", 0)
        ]);

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Warnings, warning => warning.Contains("Одинаковый заголовок", StringComparison.CurrentCultureIgnoreCase));
        Assert.Contains("Окна, вариант 1", result.Warnings.Single());
        Assert.Contains("Окна, вариант 2", result.Warnings.Single());
    }

    [Fact]
    public void Aggregate_DeduplicatesSameSegmentInstanceAndSortsNaturalSheetNumbers()
    {
        ScheduleRegisterAggregationResult result = service.Aggregate(
        [
            Placement(20, "Двери", "Спецификация дверей", 102, "10", 0),
            Placement(10, "Окна", "Спецификация окон", 101, "2", 0),
            Placement(10, "Окна", "Спецификация окон", 101, "2", 0)
        ]);

        Assert.Equal(["2", "10"], result.Rows.Select(row => row.SheetNumbers));
        Assert.Empty(result.Warnings);
    }

    private static SchedulePlacementSnapshot Placement(
        long scheduleId,
        string browserName,
        string title,
        long sheetId,
        string sheetNumber,
        int segmentIndex)
    {
        return new SchedulePlacementSnapshot(
            scheduleId,
            browserName,
            title,
            sheetId,
            sheetNumber,
            segmentIndex);
    }
}
