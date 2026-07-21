using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleDiagnosticGuidanceBuilderTests
{
    [Fact]
    public void Build_SummarizesCeilingFailuresAndOffersResolutionOptions()
    {
        FinishSchedulePreviewResult preview = Preview(
        [
            Warning(10, 100),
            Warning(11, 100),
            Warning(11, 101)
        ]);

        IReadOnlyList<string> guidance = FinishScheduleDiagnosticGuidanceBuilder.Build(preview);

        Assert.Contains(guidance, item => item.Contains(
            "проблемных элементов — 2; затронуто помещений — 2",
            StringComparison.Ordinal));
        Assert.Contains(guidance, item => item.Contains("верхнюю границу помещений", StringComparison.Ordinal));
        Assert.Contains(guidance, item => item.Contains("делить его по помещениям не требуется", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ExplainsZeroLinksEvenWithoutCriticalWarnings()
    {
        FinishSchedulePreviewResult preview = Preview([]);

        IReadOnlyList<string> guidance = FinishScheduleDiagnosticGuidanceBuilder.Build(preview);

        Assert.Contains(guidance, item => item.Contains(
            "Потолки не связаны с помещениями",
            StringComparison.Ordinal));
    }

    private static FinishSchedulePreviewResult Preview(
        IReadOnlyList<FinishGeometryWarning> geometryWarnings)
    {
        return new FinishSchedulePreviewResult(
            2,
            new FinishRoomScopeResult(
                [Room(10), Room(11)],
                [],
                0,
                0),
            new FinishPreviewCategoryCounts(0, 0, 0),
            new FinishPreviewCategoryCounts(0, 0, 0),
            new FinishPreviewCategoryCounts(4, 2, 2),
            new FinishPreviewIndexCounts(2, 0, 4),
            geometryWarnings.Select(warning => warning.Message),
            new FinishQuantityPreviewSummary(
                new FinishQuantityCategorySummary(0, 0, 0, 0),
                new FinishQuantityCategorySummary(0, 0, 0, 0),
                new FinishQuantityCategorySummary(0, 0, 0, 0)),
            geometryWarnings: geometryWarnings);
    }

    private static FinishGeometryWarning Warning(long roomId, long elementId)
    {
        return new FinishGeometryWarning(
            FinishGeometryWarningCode.SlabGeometryUnsupported,
            $"Потолок {elementId} пропущен.",
            roomId,
            elementId,
            FinishPreviewCategory.Ceilings);
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
