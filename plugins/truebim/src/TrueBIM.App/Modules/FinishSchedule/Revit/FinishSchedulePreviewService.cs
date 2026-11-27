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
        FinishScheduleSettings settings) => BuildDetailed(document, settings, []);

    internal FinishScheduleCalculationResult BuildDetailed(
        Document document,
        FinishScheduleSettings settings,
        IEnumerable<FinishGeometryWarning> roomGeometryWarnings)
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
        FinishQuantityRequest request = new(
            new RoomScopeService().Select(collection.Rooms, ReportScopeSettings.EntireProject()).SelectedRooms,
            build.Classification.Elements);
        HashSet<long> selectedRoomIds = new(build.RoomScope.SelectedRooms.Select(room => room.ElementId));
        FinishParameterTargetBuildResult ownershipTargets = new(0, [], []);
        FinishQuantityResult? geometry = null;
        HashSet<long> touchedFloorCorrections = [];
        if (settings.WriteOwnership)
        {
            // Resolve against all rooms before restricting writes to the selected report scope.
            geometry = new PhysicalFinishQuantitySource(document, logger).Calculate(request);
            HashSet<long> outsideGeometryIds = new(geometry.Occurrences
                .Where(item => !selectedRoomIds.Contains(item.RoomId)).Select(item => item.ElementId));
            HashSet<long> selectedGeometryIds = new(geometry.Occurrences
                .Where(item => selectedRoomIds.Contains(item.RoomId)).Select(item => item.ElementId));
            // A room whose sliver moves to a neighbour can still have an assignment saved
            // by an older version. Rewrite that shared floor even when only this room is in scope.
            touchedFloorCorrections.UnionWith(geometry.Warnings.Where(w => w.SmallContactResolved
                    && w.Category == FinishPreviewCategory.Floors && w.ElementId.HasValue
                    && w.CompetingContacts.Any(c => selectedRoomIds.Contains(c.RoomId)))
                .Select(w => w.ElementId!.Value));
            selectedGeometryIds.UnionWith(touchedFloorCorrections);
            outsideGeometryIds.ExceptWith(selectedGeometryIds);
            // Outside the room bounds a project or level scope can still own an unassigned element.
            // An element assigned by geometry exclusively to another scope must keep its owner.
            FinishClassifiedElement[] assignmentElements = build.InScopeElements
                .Concat(build.Classification.Elements.Where(item => selectedGeometryIds.Contains(item.Element.ElementId)
                    || settings.Scope.Kind == ReportScopeKind.EntireProject
                    || (settings.Scope.Kind == ReportScopeKind.Level && item.Element.LevelId == settings.Scope.LevelId)))
                .Where(item => !outsideGeometryIds.Contains(item.Element.ElementId))
                .GroupBy(item => item.Element.ElementId).Select(group => group.First()).ToArray();
            RoomFinishSnapshotBuildResult identifiers = new RoomFinishSnapshotBuilder(
                new FinishDescriptionNormalizer()).Build(new RoomFinishSnapshotRequest(
                    settings, request.Rooms, build.Classification.Elements, collection.Types, geometry));
            ownershipTargets = new FinishOwnershipValueBuilder().Build(
                settings, assignmentElements, geometry, identifiers, request);
            timings.Add(Timing(FinishScheduleStageNames.PhysicalQuantities, stageTimer));
            stageTimer.Restart();
        }

        // Preview uses proposed parameter values; Apply reads them back after the assignment transaction.
        FinishQuantityResult stored = new StoredFinishQuantitySource(document, settings,
            ownershipTargets.Targets.ToDictionary(target => target.ElementId, target => target.Value),
            ownershipTargets.RoomAssignments.ToDictionary(item => item.ElementId, item => item.RoomId),
            ownershipTargets.RoomAssignments.ToDictionary(item => item.ElementId, item => item.FloorAreas)).Calculate(request);
        var wallAreas = stored.Occurrences.Where(o => o.Category == FinishPreviewCategory.Walls).ToLookup(o => o.RoomId);
        var geometryWarnings = (geometry?.Warnings ?? []).Concat(roomGeometryWarnings).Select(warning =>
        {
            if (warning.Code != FinishGeometryWarningCode.WallFaceRecovered || warning.RoomId is not long roomId)
                return warning;
            var walls = wallAreas[roomId];
            string outcome = walls.Any()
                ? $"Ведомость учитывает стены этого помещения: {walls.Count()} шт., {walls.Sum(o => o.AreaSquareMeters):0.00} м²."
                : $"Отделочные стены в ведомость этого помещения не попали. Если ожидаете отделку, проверьте стены в активной модели и значение «{settings.Walls.ClassificationValue}» в параметре типа «{FinishScheduleSettings.ClassificationParameterName}».";
            return warning with { Message = warning.Message + " " + outcome };
        }).ToArray();
        var unavailableFloors = geometryWarnings.Where(w => w.Code == FinishGeometryWarningCode.ProjectedAreaUnavailable
                && w.Category == FinishPreviewCategory.Floors && w.ElementId.HasValue)
            .Select(w => w.ElementId!.Value).ToHashSet();
        FinishQuantityResult allQuantities = new(stored.Occurrences.Where(o => !unavailableFloors.Contains(o.ElementId)),
            stored.Warnings.Concat(geometryWarnings).Distinct(),
            geometry?.CacheMetrics, geometry?.Contacts);
        HashSet<long> outsideOwnedIds = new(allQuantities.Occurrences
            .Where(item => !selectedRoomIds.Contains(item.RoomId)).Select(item => item.ElementId));
        HashSet<long> selectedOwnedIds = new(allQuantities.Occurrences
            .Where(item => selectedRoomIds.Contains(item.RoomId)).Select(item => item.ElementId));
        selectedOwnedIds.UnionWith(touchedFloorCorrections);
        outsideOwnedIds.ExceptWith(selectedOwnedIds);
        var roomSearchIds = build.InScopeElements.Select(e => e.Element.ElementId).ToHashSet();
        build = build with
        {
            InScopeElements = build.InScopeElements.Concat(build.Classification.Elements
                    .Where(item => selectedOwnedIds.Contains(item.Element.ElementId)))
                .Where(item => !outsideOwnedIds.Contains(item.Element.ElementId))
                .GroupBy(item => item.Element.ElementId).Select(group => group.First()).ToArray()
        };
        HashSet<long> scopeElementIds = new(build.InScopeElements.Select(item => item.Element.ElementId));
        // Report spatial exclusion independently of which ownership parameters need updating.
        var outsideLevelSearch = settings.Scope.Kind == ReportScopeKind.Level && selectedRoomIds.Count > 0
            ? build.Classification.Elements.Where(e => e.Element.LevelId == settings.Scope.LevelId && e.Element.Bounds is not null
                && !roomSearchIds.Contains(e.Element.ElementId) && !selectedOwnedIds.Contains(e.Element.ElementId))
                .Select(e => new FinishGeometryWarning(FinishGeometryWarningCode.OutsideLevelRoomSearch,
                    $"Элемент {e.Element.ElementId} привязан к выбранному уровню и находится вне области поиска его помещений.",
                    ElementId: e.Element.ElementId, Category: e.Category)
                    { OwnershipFinding = FinishOwnershipFinding.OutsideLevelRoomSearch }).ToArray()
            : [];
        FinishQuantityResult quantities = new(
            allQuantities.Occurrences.Where(item => selectedRoomIds.Contains(item.RoomId)),
            allQuantities.Warnings.Where(warning => settings.Scope.Kind == ReportScopeKind.EntireProject
                || (warning.RoomId.HasValue
                    ? selectedRoomIds.Contains(warning.RoomId.Value) || (warning.SmallContactResolved
                        && warning.Category == FinishPreviewCategory.Floors && warning.CompetingContacts.Any(c => selectedRoomIds.Contains(c.RoomId)))
                    : !warning.ElementId.HasValue || scopeElementIds.Contains(warning.ElementId.Value))).Concat(outsideLevelSearch),
            allQuantities.CacheMetrics,
            allQuantities.Contacts);
        timings.Add(Timing(FinishScheduleStageNames.StoredQuantities, stageTimer));
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
                new FinishAggregationFormatter(
                    settings.EffectiveColumnWidths.DescriptionMillimeters,
                    FinishScheduleTextLayout.CreateLineCounter(document,
                        settings.EffectiveColumnWidths.DescriptionMillimeters))).Aggregate(roomSnapshots);
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
                    + $"Category={warning.Category?.ToString() ?? "-"}; {warning.Message} {warning.TechnicalDetail}".TrimEnd());
        }

        return new FinishScheduleCalculationResult(
            result,
            collection,
            build,
            quantities,
            roomSnapshots,
            aggregation,
            ownershipTargets);
    }

    private static FinishScheduleStageTiming Timing(string stage, Stopwatch timer)
    {
        timer.Stop();
        return new FinishScheduleStageTiming(stage, timer.ElapsedMilliseconds);
    }
}
