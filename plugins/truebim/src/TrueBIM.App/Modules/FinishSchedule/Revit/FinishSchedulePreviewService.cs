using System.Diagnostics;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishSchedulePreviewService
{
    private readonly FinishElementCollector collector;
    private readonly FinishSchedulePreviewBuilder builder;
    private readonly ITrueBimLogger logger;

    public FinishSchedulePreviewService(
        FinishElementCollector collector,
        FinishSchedulePreviewBuilder builder,
        ITrueBimLogger logger)
    {
        this.collector = collector ?? throw new ArgumentNullException(nameof(collector));
        this.builder = builder ?? throw new ArgumentNullException(nameof(builder));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FinishSchedulePreviewResult Build(
        Document document,
        FinishScheduleSettings settings)
    {
        return BuildDetailed(document, settings).Preview;
    }

    public FinishScheduleCalculationResult BuildDetailed(
        Document document,
        FinishScheduleSettings settings)
    {
        Stopwatch totalTimer = Stopwatch.StartNew();
        Stopwatch stageTimer = Stopwatch.StartNew();
        List<FinishScheduleStageTiming> timings = [];
        FinishElementCollection collection = collector.Collect(document, settings);
        timings.Add(Timing(FinishScheduleStageNames.CollectCandidates, stageTimer));

        stageTimer.Restart();
        FinishSchedulePreviewBuild build = builder.BuildDetailed(collection, settings);
        timings.Add(Timing(FinishScheduleStageNames.ScopeAndIndex, stageTimer));

        stageTimer.Restart();
        IFinishQuantitySource quantitySource = new PhysicalFinishQuantitySource(document, logger);
        FinishQuantityResult quantities = quantitySource.Calculate(new FinishQuantityRequest(
            build.RoomScope.SelectedRooms,
            build.InScopeElements));
        timings.Add(Timing(FinishScheduleStageNames.PhysicalQuantities, stageTimer));
        FinishSchedulePreviewResult result = build.Preview.WithQuantities(quantities);
        RoomFinishSnapshotBuildResult? roomSnapshots = null;
        FinishAggregationResult? aggregation = null;
        stageTimer.Restart();
        if (settings.DescriptionParameter is not null
            && (settings.RoomIdentifier.Mode != RoomIdentifierMode.CustomParameter
                || settings.RoomIdentifier.CustomParameter is not null))
        {
            roomSnapshots = new RoomFinishSnapshotBuilder(
                new FinishDescriptionNormalizer()).Build(new RoomFinishSnapshotRequest(
                    settings,
                    build.RoomScope.SelectedRooms,
                    build.Classification.Elements,
                    collection.Types,
                    quantities));
            aggregation = new FinishAggregationService(
                new FinishGroupKeyBuilder(),
                new FinishAggregationFormatter()).Aggregate(roomSnapshots);
            result = result.WithAggregation(aggregation);
        }

        timings.Add(Timing(FinishScheduleStageNames.Aggregation, stageTimer));
        totalTimer.Stop();
        timings.Add(new FinishScheduleStageTiming(
            FinishScheduleStageNames.TotalCalculation,
            totalTimer.ElapsedMilliseconds));
        FinishSchedulePerformanceSummary performance = new(
            timings,
            new FinishScheduleCacheSummary(collection.Types.Count, quantities.CacheMetrics));
        result = result.WithPerformance(performance);

        logger.Info(
            $"Finish Schedule preview built. Rooms={result.RoomScope.SelectedRooms.Count}/{result.CollectedRooms}; "
            + $"Walls={result.Walls.InScope}; Floors={result.Floors.InScope}; Ceilings={result.Ceilings.InScope}; "
            + $"Pairs={result.Index.PotentialRoomElementPairs}; Occurrences={quantities.Occurrences.Count}; "
            + $"GeometryWarnings={quantities.Warnings.Count}; Groups={aggregation?.Groups.Count ?? 0}; "
            + $"AggregationWarnings={aggregation?.Warnings.Count ?? 0}; "
            + $"ElapsedMs={totalTimer.ElapsedMilliseconds}; TypeCache={collection.Types.Count}; "
            + $"ElementGeometryCacheHits={quantities.CacheMetrics.ElementHits}.");
        foreach (FinishGeometryWarning warning in quantities.Warnings)
        {
            string impact = FinishGeometryWarningClassifier.AffectsScheduleValue(warning)
                ? "critical"
                : "diagnostic";
            logger.Warning(
                $"Finish Schedule geometry warning [{impact}] {warning.Code}; "
                    + $"Room={warning.RoomId?.ToString() ?? "-"}; Element={warning.ElementId?.ToString() ?? "-"}; "
                    + $"Category={warning.Category?.ToString() ?? "-"}; {warning.Message}");
        }

        return new FinishScheduleCalculationResult(
            result,
            collection,
            build,
            quantities,
            roomSnapshots,
            aggregation);
    }

    private static FinishScheduleStageTiming Timing(string stage, Stopwatch timer)
    {
        timer.Stop();
        return new FinishScheduleStageTiming(stage, timer.ElapsedMilliseconds);
    }
}
