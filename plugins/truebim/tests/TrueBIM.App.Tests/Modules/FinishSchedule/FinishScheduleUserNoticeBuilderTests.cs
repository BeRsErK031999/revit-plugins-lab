using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleUserNoticeBuilderTests
{
    private readonly FinishScheduleUserNoticeBuilder builder = new();

    [Fact]
    public void BuildPreview_ReplacesRawRevitExceptionWithFriendlyGroupedWarning()
    {
        const string rawMessage = "Failed to perform the Boolean operation for the two solids.";
        FinishGeometryWarning geometryWarning = new(
            FinishGeometryWarningCode.BooleanIntersectionFailed,
            rawMessage,
            RoomId: 2304268,
            ElementId: 1957984,
            Category: FinishPreviewCategory.Ceilings);
        FinishSchedulePreviewResult preview = CreatePreview([rawMessage], [geometryWarning]);

        FinishScheduleUserNotice notice = builder.BuildPreview(
            preview,
            FinishScheduleSettings.CreateDefault());

        Assert.Equal(FinishScheduleUserNoticeSeverity.Warning, notice.Severity);
        Assert.Equal(1, notice.IssueCount);
        Assert.DoesNotContain(notice.WarningItems, item => item.Contains(rawMessage, StringComparison.Ordinal));
        Assert.Contains(notice.WarningItems, item => item.Contains("Сложных пересечений потолков: 1", StringComparison.Ordinal));
        Assert.Contains(notice.WarningItems, item => item.Contains("2304268", StringComparison.Ordinal));
        Assert.Contains(notice.WarningItems, item => item.Contains("1957984", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPreview_ShowsOnlyUsefulCalculationSummary()
    {
        FinishScheduleUserNotice notice = builder.BuildPreview(
            CreatePreview([], []),
            FinishScheduleSettings.CreateDefault());

        Assert.Equal(FinishScheduleUserNoticeSeverity.Success, notice.Severity);
        Assert.Empty(notice.WarningItems);
        Assert.Contains(notice.SummaryItems, item => item == "Помещения: рассчитано 2 из 2.");
        Assert.Contains(notice.SummaryItems, item => item.Contains("Полы: найдено элементов — 1", StringComparison.Ordinal));
        Assert.DoesNotContain(notice.SummaryItems, item => item.Contains("Spatial", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(notice.SummaryItems, item => item.Contains("cache", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(notice.SummaryItems, item => item.Contains("классифицировано", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPreview_DoesNotTellUserToSplitMultiRoomFloor()
    {
        FinishScheduleUserNotice notice = builder.BuildPreview(
            CreatePreview([], []),
            FinishScheduleSettings.CreateDefault());

        Assert.DoesNotContain(notice.WarningItems, item => item.Contains("раздел", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(notice.SummaryItems, item => item.Contains("участков в помещениях — 2", StringComparison.Ordinal));
    }

    private static FinishSchedulePreviewResult CreatePreview(
        IReadOnlyList<string> warnings,
        IReadOnlyList<FinishGeometryWarning> geometryWarnings)
    {
        return new FinishSchedulePreviewResult(
            2,
            new FinishRoomScopeResult(
                [Room(10), Room(11)],
                [],
                0,
                0),
            new FinishPreviewCategoryCounts(3, 3, 3),
            new FinishPreviewCategoryCounts(1, 1, 1),
            new FinishPreviewCategoryCounts(1, 1, 1),
            new FinishPreviewIndexCounts(5, 0, 8),
            warnings,
            new FinishQuantityPreviewSummary(
                new FinishQuantityCategorySummary(3, 2, 3, 25.5),
                new FinishQuantityCategorySummary(2, 2, 1, 12.25),
                new FinishQuantityCategorySummary(2, 2, 1, 12.25)),
            geometryWarnings: geometryWarnings);
    }

    private static FinishRoomCandidateSnapshot Room(long id)
    {
        return new FinishRoomCandidateSnapshot(
            id,
            1,
            10,
            true,
            new AxisAlignedBox3D(0, 0, 0, 2, 2, 3));
    }
}
