using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleUserNoticeBuilderTests
{
    private readonly FinishScheduleUserNoticeBuilder builder = new();

    [Fact]
    public void BuildPreview_ReportsEachMissingStoredNumberWithItsElementIds()
    {
        StoredFinishElement[] elements = Enumerable.Range(1, 18)
            .Select(id => new StoredFinishElement(id, FinishPreviewCategory.Walls, 1, id <= 4 ? "802" : "320", 5))
            .ToArray();
        FinishQuantityResult quantities = new StoredFinishQuantityCalculator().Calculate(
            elements, [Room(100), Room(101)], RoomIdentifierSettings.ByNumber());
        FinishScheduleUserNotice notice = builder.BuildPreview(
            CreatePreview(quantities.Warnings.Select(warning => warning.Message).ToArray(), quantities.Warnings),
            FinishScheduleSettings.CreateDefault() with { WriteOwnership = false });

        Assert.Equal(FinishScheduleUserNoticeSeverity.Warning, notice.Severity);
        Assert.Equal(3, notice.WarningItems.Count);
        Assert.Contains(notice.WarningItems, item => item.Contains("«802»") && item.Contains("не найдено")
            && item.Contains("не учтена (4)") && item.Contains("Элементы: 1, 2, 3, 4."));
        Assert.Contains(notice.WarningItems, item => item.Contains("«320»") && item.Contains("не учтена (14)"));
        Assert.Contains(notice.WarningItems, item => item.Contains("стандартным параметром «Номер» самих помещений"));
        Assert.DoesNotContain(notice.WarningItems, item => item.Contains("геометр", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPreview_WarnsAboutEmptyOwnershipButAcceptsRepeatedIdentifiers()
    {
        FinishQuantityResult quantities = new StoredFinishQuantityCalculator().Calculate(
            [new StoredFinishElement(1, FinishPreviewCategory.Walls, 1, "", 5),
                new StoredFinishElement(2, FinishPreviewCategory.Walls, 1, "101", 5)],
            [new FinishRoomCandidateSnapshot(100, 1, 10, true, null, number: "101"),
                new FinishRoomCandidateSnapshot(101, 1, 10, true, null, number: "101")],
            RoomIdentifierSettings.ByNumber());
        FinishScheduleUserNotice notice = builder.BuildPreview(
            CreatePreview([], quantities.Warnings), FinishScheduleSettings.CreateDefault() with { WriteOwnership = false });

        Assert.Contains(notice.WarningItems, item => item.Contains("не заполнена") && item.Contains("ID элементов (1): 1."));
        Assert.DoesNotContain(notice.WarningItems, item => item.Contains("неоднозначна") || item.Contains("ID элементов (1): 2."));
        Assert.Equal(1, notice.IssueCount);
        Assert.Equal(2, Assert.Single(quantities.Occurrences).ElementId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildPreview_GeometricMissingLinkAdviceRequiresAutomaticAssignment(bool writeOwnership)
    {
        FinishScheduleUserNotice notice = builder.BuildPreview(
            CreatePreview([], [], slabOccurrences: 0),
            FinishScheduleSettings.CreateDefault() with { WriteOwnership = writeOwnership });

        Assert.Equal(writeOwnership, notice.WarningItems.Any(item => item.Contains("нижнюю границу")));
        Assert.Equal(writeOwnership, notice.WarningItems.Any(item => item.Contains("верхнюю границу")));
    }

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
        Assert.Contains(notice.SummaryItems, item => item == "Размещённые помещения: рассчитано 2 из 2.");
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

    [Fact]
    public void AllReportStages_GroupTypeIdsAndContactFailuresWithoutHidingOtherIssues()
    {
        var settings = FinishScheduleSettings.CreateDefault();
        FinishGeometryWarning[] warnings =
        [
            new(FinishGeometryWarningCode.UnassignedElement, "contact-42", ElementId: 42) { OwnershipFinding = FinishOwnershipFinding.NoContact },
            new(FinishGeometryWarningCode.UnassignedElement, "contact-41", ElementId: 41) { OwnershipFinding = FinishOwnershipFinding.NoContact },
            new(FinishGeometryWarningCode.StoredOwnershipUnresolved, "empty-41", ElementId: 41) { OwnershipFinding = FinishOwnershipFinding.EmptyParameter },
            new(FinishGeometryWarningCode.StoredOwnershipUnresolved, "Неверный идентификатор «802».", ElementId: 43)
        ];
        var preview = CreatePreview(warnings.Select(w => w.Message).ToArray(), warnings).WithAggregation(
            new FinishAggregationResult([], new[] { (50, 700), (51, 600), (52, 700) }.Select(e =>
                new FinishAggregationWarning(FinishAggregationWarningCode.MissingDescription, $"description-{e.Item1}", 10, e.Item1)
                    { TypeId = e.Item2 })));
        var write = new FinishScheduleWritePreview(1, 2, FinishWritePlan.Empty(), FinishWritePlan.Empty(
            [new FinishWriteIssue(FinishWriteIssueCode.UnknownOwnership, FinishWriteIssueSeverity.Warning, "write-41", 41)]),
            preview.Warnings, calculation: preview, settings: settings);
        var result = new FinishScheduleWriteResult(FinishScheduleWriteStatus.Applied, 1, 0, 1, preview.Warnings.Append("write-41").ToArray(), "Готово.");
        var reports = new FinishScheduleReportBuilder();
        foreach (string report in new[] { reports.BuildPreview(preview, settings), reports.BuildWritePreview(write),
                     reports.BuildResult(write, result), reports.BuildSummary(builder.BuildPreview(preview, settings)),
                     reports.BuildSummary(builder.BuildWritePreview(write, settings)), reports.BuildSummary(builder.BuildResult(write, result, settings)) })
        {
            Assert.Single(report.Split('\n'), line => line.Contains("ID элементов (2): 41, 42."));
            Assert.Single(report.Split('\n'), line => line.Contains("ID типоразмеров (2): 600, 700."));
            Assert.Contains("«802»", report);
            foreach (string raw in new[] { "contact-", "empty-41", "description-", "write-41" }) Assert.DoesNotContain(raw, report);
        }
    }

    [Fact]
    public void OutsideLevelRoomSearch_IsInformationalAndDoesNotMakeCalculationIncomplete()
    {
        var warning = new FinishGeometryWarning(FinishGeometryWarningCode.OutsideLevelRoomSearch, "outside-55", ElementId: 55)
            { OwnershipFinding = FinishOwnershipFinding.OutsideLevelRoomSearch };
        var preview = CreatePreview([warning.Message], [warning]);
        var settings = FinishScheduleSettings.CreateDefault();
        var notice = builder.BuildPreview(preview, settings);
        Assert.Equal(FinishScheduleUserNoticeSeverity.Success, notice.Severity);
        Assert.Equal(0, notice.IssueCount);
        Assert.Contains(notice.SummaryItems, item => item.Contains("ID элементов (1): 55."));
        var report = new FinishScheduleReportBuilder().BuildPreview(preview, settings);
        Assert.Contains("ПРЕДУПРЕЖДЕНИЯ (0)", report);
        Assert.DoesNotContain("outside-55", report);
    }

    private static FinishSchedulePreviewResult CreatePreview(
        IReadOnlyList<string> warnings,
        IReadOnlyList<FinishGeometryWarning> geometryWarnings,
        int slabOccurrences = 2)
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
                new FinishQuantityCategorySummary(slabOccurrences, 2, 1, 12.25),
                new FinishQuantityCategorySummary(slabOccurrences, 2, 1, 12.25)),
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
