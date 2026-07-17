using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleReportBuilderTests
{
    [Fact]
    public void BuildPreview_IncludesCountsAllWarningsTimingsAndCacheMetrics()
    {
        FinishSchedulePreviewResult preview = Preview();

        string report = new FinishScheduleReportBuilder().BuildPreview(
            preview,
            FinishScheduleSettings.CreateDefault());

        Assert.Contains("найдено 6; обработано 2; невалидных 1; вне scope 3", report);
        Assert.Contains("проверено потенциальных пар 4", report);
        Assert.Contains("Сбор кандидатов: 12 мс", report);
        Assert.Contains("Type cache: entries 7", report);
        Assert.Contains("Element geometry cache: entries 5; requests 9; hits 4", report);
        Assert.Contains("1. Первое предупреждение.", report);
        Assert.Contains("2. Последнее предупреждение.", report);
    }

    [Fact]
    public void BuildResult_IncludesWriteScheduleApplyTimingAndCompleteWarnings()
    {
        FinishSchedulePreviewResult calculation = Preview();
        FinishScheduleWritePreview preview = new(
            2,
            2,
            FinishWritePlan.Empty(),
            FinishWritePlan.Empty(),
            calculation.Warnings,
            calculation: calculation);
        FinishScheduleWriteResult result = new(
            FinishScheduleWriteStatus.Applied,
            6,
            4,
            1,
            ["Последнее предупреждение.", "Write warning."],
            "Готово.",
            new FinishRoomScheduleApplyResult(
                900,
                "Помещения • Ведомость отделки помещений",
                FinishRoomScheduleAction.Update),
            new FinishSchedulePerformanceSummary(
            [
                new FinishScheduleStageTiming(FinishScheduleStageNames.TotalApply, 25)
            ]));

        string report = new FinishScheduleReportBuilder().BuildResult(preview, result);

        Assert.Contains("Статус: ВЫПОЛНЕНО", report);
        Assert.Contains("Room-значений записано: 6", report);
        Assert.Contains("id 900; действие Update", report);
        Assert.Contains("Итого применение: 25 мс", report);
        Assert.Contains("Write warning.", report);
    }

    [Fact]
    public void StageTiming_RejectsNegativeDuration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FinishScheduleStageTiming(FinishScheduleStageNames.Aggregation, -1));
    }

    private static FinishSchedulePreviewResult Preview()
    {
        FinishRoomCandidateSnapshot[] selected = [Room(1), Room(2)];
        return new FinishSchedulePreviewResult(
            6,
            new FinishRoomScopeResult(
                selected,
                [new FinishRoomScopeSkip(3, FinishRoomSkipReason.Unplaced)],
                3,
                0),
            new FinishPreviewCategoryCounts(10, 8, 4),
            new FinishPreviewCategoryCounts(6, 5, 2),
            new FinishPreviewCategoryCounts(6, 3, 1),
            new FinishPreviewIndexCounts(16, 2, 4),
            ["Первое предупреждение.", "Последнее предупреждение."],
            new FinishQuantityPreviewSummary(
                new FinishQuantityCategorySummary(4, 2, 4, 18.25),
                new FinishQuantityCategorySummary(2, 2, 2, 12.5),
                new FinishQuantityCategorySummary(1, 1, 1, 7.75)),
            new FinishAggregationPreviewSummary(2, 2, 0),
            new FinishSchedulePerformanceSummary(
            [
                new FinishScheduleStageTiming(FinishScheduleStageNames.CollectCandidates, 12),
                new FinishScheduleStageTiming(FinishScheduleStageNames.TotalCalculation, 40)
            ],
            new FinishScheduleCacheSummary(
                7,
                new FinishGeometryCacheMetrics(2, 2, 0, 9, 5, 4))));
    }

    private static FinishRoomCandidateSnapshot Room(long id)
    {
        return new FinishRoomCandidateSnapshot(
            id,
            10,
            20,
            true,
            new AxisAlignedBox3D(0, 0, 0, 10, 10, 3));
    }
}
