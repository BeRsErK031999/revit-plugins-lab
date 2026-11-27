using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class PhysicalFinishQuantitySource : IFinishQuantitySource
{
    private const double ProbeThickness = 0.05;
    private const double HorizontalNormalTolerance = 0.999;
    private const double VerticalNormalTolerance = 0.01;
    private const double MinimumCeilingVerticalNormalComponent = 0.01;
    private const double MinimumIntersectionVolume = 1e-10;
    private const double MinimumAreaSquareMeters = 1e-8;
    private const double ParallelWallNormalTolerance = 0.98;

    private readonly Document document;
    private readonly ITrueBimLogger logger;

    public PhysicalFinishQuantitySource(Document document, ITrueBimLogger logger)
    {
        this.document = document ?? throw new ArgumentNullException(nameof(document));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public FinishQuantityResult Calculate(FinishQuantityRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        FinishOccurrenceAccumulator accumulator = new();
        List<FinishGeometryWarning> warnings = [];
        HashSet<FinishGeometryWarning> warningKeys = [];
        FinishBoundingBoxIndex index = new(request.Elements);
        FinishRoomSearchBounds searchBounds = new(request.Rooms, request.Elements);
        var obstacles = new FinishVerticalObstacles(document);
        HashSet<long> consideredElementIds = [];
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedWalls = request.Elements
            .Where(element => element.Category == FinishPreviewCategory.Walls)
            .GroupBy(element => element.Element.ElementId)
            .ToDictionary(group => group.Key, group => group.First());
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedFloors = request.Elements
            .Where(element => element.Category == FinishPreviewCategory.Floors)
            .GroupBy(element => element.Element.ElementId)
            .ToDictionary(group => group.Key, group => group.First());
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedCeilings = request.Elements
            .Where(element => element.Category == FinishPreviewCategory.Ceilings)
            .GroupBy(element => element.Element.ElementId)
            .ToDictionary(group => group.Key, group => group.First());
        FinishElementGeometryCache elementGeometryCache = new(document);

        using FinishRoomGeometryCache roomGeometryCache = new(document);
        foreach (FinishRoomCandidateSnapshot room in request.Rooms)
        {
            if (!roomGeometryCache.TryGet(room.ElementId, out FinishRoomGeometryData? roomGeometry, out FinishGeometryWarning? warning)
                || roomGeometry is null)
            {
                if (warning is not null)
                {
                    AddWarning(warnings, warningKeys, warning);
                }

                continue;
            }

            FinishBoundaryElementIds directBoundaryIds = CalculateBoundaryQuantities(
                room.ElementId,
                roomGeometry,
                classifiedWalls,
                classifiedFloors,
                classifiedCeilings,
                accumulator,
                warnings,
                warningKeys);
            IReadOnlyList<FinishClassifiedElement> candidates = room.Bounds is null
                ? []
                : QueryCandidates(index, searchBounds, room).Where(e => CanReachFromRoom(room, e, obstacles)).ToArray();
            consideredElementIds.UnionWith(candidates.Select(candidate => candidate.Element.ElementId));
            CalculateFallbackWalls(
                room.ElementId,
                roomGeometry,
                candidates,
                directBoundaryIds.Walls,
                elementGeometryCache,
                accumulator,
                warnings,
                warningKeys);
            CalculateSlabs(
                room.ElementId,
                roomGeometry,
                candidates,
                FinishPreviewCategory.Floors,
                directBoundaryIds.Floors,
                elementGeometryCache,
                accumulator,
                warnings,
                warningKeys);
            CalculateSlabs(
                room.ElementId,
                roomGeometry,
                candidates,
                FinishPreviewCategory.Ceilings,
                directBoundaryIds.Ceilings,
                elementGeometryCache,
                accumulator,
                warnings,
                warningKeys);
            new FinishProjectedContactSource().AddContacts(room, roomGeometry, candidates,
                searchBounds, elementGeometryCache, accumulator, warnings);
        }

        Dictionary<long, double> fullAreas = request.Elements
            .Select(item => item.Element.ElementId)
            .Distinct()
            .ToDictionary(id => id, id =>
            {
                Parameter? area = document.GetElement(RevitElementIds.Create(id))
                    ?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                return area?.StorageType == StorageType.Double
                    ? RevitAreaUnits.ToSquareMeters(area.AsDouble())
                    : 0;
            });
        Dictionary<long, FinishRoomCandidateSnapshot> roomsById = request.Rooms.ToDictionary(room => room.ElementId);
        Dictionary<long, FinishClassifiedElement> elementsById = request.Elements.ToDictionary(item => item.Element.ElementId);
        HashSet<long> occupiedLevels = new(request.Rooms.Select(room => room.LevelId));
        Dictionary<long, long> wallLevels = classifiedWalls.Keys.ToDictionary(id => id,
            id => RevitElementIds.GetValue(document.GetElement(RevitElementIds.Create(id)).LevelId));
        FinishOccurrenceAccumulator eligible = new();
        foreach (FinishOccurrence contact in accumulator.Build())
        {
            if (contact.Category == FinishPreviewCategory.Walls && occupiedLevels.Contains(wallLevels[contact.ElementId])
                && roomsById[contact.RoomId].LevelId != wallLevels[contact.ElementId]) continue;
            if (searchBounds.Includes(roomsById[contact.RoomId], elementsById[contact.ElementId]) && CanReachFromRoom(roomsById[contact.RoomId], elementsById[contact.ElementId], obstacles))
                eligible.KeepLargest(contact);
        }
        IReadOnlyDictionary<long, long> contourOwners = new FinishConnectedWallContactSource().AddContacts(document, request, eligible);
        FinishOffLevelContactSource.AddContacts(document, request, roomGeometryCache, eligible);
        FinishOccurrence[] contacts = eligible.Build().ToArray();
        ConfirmBoundedWallContacts(warnings, contacts, elementsById, roomsById);
        FinishQuantityResult floors = FinishFloorRoomAreas.Calculate(document, request, obstacles);
        var otherContacts = contacts.Where(item => item.Category != FinishPreviewCategory.Floors).ToArray();
        FinishQuantityResult resolved = new FinishElementOwnershipResolver().Resolve(otherContacts, fullAreas, contourOwners);
        // Slab probes rank candidates. The upper-surface partition is authoritative for floors.
        warnings.RemoveAll(w => w.Category == FinishPreviewCategory.Floors);
        warnings.AddRange(floors.Warnings);
        warnings.AddRange(new FinishUnassignedElementWarningBuilder().Build(request.Elements.Where(e => e.Category != FinishPreviewCategory.Floors),
            otherContacts.Concat(floors.Occurrences), consideredElementIds));
        FinishQuantityResult result = new(
            resolved.Occurrences.Concat(floors.Occurrences),
            warnings.Where(warning => !IsRecoveredWarning(warning, contacts)).Concat(resolved.Warnings),
            new FinishGeometryCacheMetrics(
                roomGeometryCache.RequestCount,
                roomGeometryCache.EntryCount,
                roomGeometryCache.HitCount,
                elementGeometryCache.RequestCount,
                elementGeometryCache.EntryCount,
                elementGeometryCache.HitCount),
            contacts);
        logger.Info(
            $"Finish Schedule physical quantities calculated. Rooms={request.Rooms.Count}; "
            + $"Occurrences={result.Occurrences.Count}; Warnings={result.Warnings.Count}; "
            + $"RoomCache={roomGeometryCache.EntryCount}/{roomGeometryCache.RequestCount}; "
            + $"ElementCache={elementGeometryCache.EntryCount}/{elementGeometryCache.RequestCount}; "
            + $"ElementCacheHits={elementGeometryCache.HitCount}.");
        return result;
    }

    private void ConfirmBoundedWallContacts(List<FinishGeometryWarning> warnings, FinishOccurrence[] contacts,
        IReadOnlyDictionary<long, FinishClassifiedElement> elements, IReadOnlyDictionary<long, FinishRoomCandidateSnapshot> rooms)
    {
        var partial = warnings.Where(w => w.UnmeasuredWallContactLimit.HasValue).ToLookup(w => w.ElementId);
        foreach (var group in partial)
        {
            if (group.Key is not long id || elements[id].Element.Bounds is not { } bounds
                || document.GetElement(RevitElementIds.Create(id)) is not Wall wall) continue;
            var measured = contacts.Where(c => c.ElementId == id).ToArray();
            double limit = group.Sum(w => w.UnmeasuredWallContactLimit!.Value);
            // Even all uncertain pieces together cannot change the owner or make this a mitred-joint tie.
            if (!FinishElementOwnershipResolver.DirectWallContactDecides(measured, limit, FinishConnectedWallContactSource.JointArea(wall, bounds))) continue;
            var confirmed = measured[0];
            foreach (var warning in group)
            {
                int index = warnings.IndexOf(warning);
                warnings[index] = warning with
                {
                    Code = FinishGeometryWarningCode.WallOwnerConfirmed,
                    Message = $"Стена {id}. Revit подтвердил контакт с помещением {rooms[confirmed.RoomId].Number} (ID {confirmed.RoomId}) на площади {confirmed.AreaSquareMeters:0.###} м². Непроверенные участки могут добавить к другим контактам не более {Math.Ceiling(limit * 1000) / 1000:0.###} м². Стена отнесена к подтверждённому помещению."
                };
            }
        }
    }

    private static bool CanReachFromRoom(FinishRoomCandidateSnapshot room, FinishClassifiedElement element, FinishVerticalObstacles obstacles)
    {
        if (element.Category == FinishPreviewCategory.Floors || room.Bounds is not { } r || element.Element.Bounds is not { } e)
            return true;
        if (element.Category == FinishPreviewCategory.Walls && room.LevelId != element.Element.LevelId &&
            !FinishCandidateSearchRules.HasWallHeightOverlap(e, r)) return false;
        if (e.MinZ <= r.MinZ) return true;
        var pad = FinishCandidateSearchRules.HorizontalToleranceInternal;
        double left = Math.Max(r.MinX - pad, e.MinX - pad), right = Math.Min(r.MaxX + pad, e.MaxX + pad);
        double bottom = Math.Max(r.MinY - pad, e.MinY - pad), top = Math.Min(r.MaxY + pad, e.MaxY + pad);
        if (right <= left || top <= bottom) return false;
        var plan = new System.Windows.Media.RectangleGeometry(new System.Windows.Rect(left, bottom, right - left, top - bottom));
        return obstacles.Visible(plan, r.MinZ + pad, e.MinZ).GetArea() > 1e-8;
    }

    private static bool IsRecoveredWarning(FinishGeometryWarning warning, IEnumerable<FinishOccurrence> contacts)
    {
        return warning.ElementId.HasValue
            && warning.Code is (FinishGeometryWarningCode.WallFallbackUnresolved
                or FinishGeometryWarningCode.SlabGeometryUnsupported
                or FinishGeometryWarningCode.ProjectedAreaUnavailable)
            && contacts.Any(contact => contact.RoomId == warning.RoomId && contact.ElementId == warning.ElementId
                && contact.Category == warning.Category && contact.Method is
                    (FinishQuantityMethod.ProjectedRoomFootprint or FinishQuantityMethod.FinishFaceRoomContact));
    }

    private static IReadOnlyList<FinishClassifiedElement> QueryCandidates(
        FinishBoundingBoxIndex index,
        FinishRoomSearchBounds searchBounds,
        FinishRoomCandidateSnapshot room)
    {
        FinishPreviewCategory[] categories =
        [
            FinishPreviewCategory.Walls,
            FinishPreviewCategory.Floors,
            FinishPreviewCategory.Ceilings
        ];
        return categories
            .SelectMany(category => index.Query(
                    searchBounds.Create(room, category))
                .Where(element => element.Category == category && searchBounds.Includes(room, element)))
            .GroupBy(element => new { element.Element.ElementId, element.Category })
            .Select(group => group.First())
            .OrderBy(element => element.Element.ElementId)
            .ThenBy(element => element.Category)
            .ToArray();
    }

    private static FinishBoundaryElementIds CalculateBoundaryQuantities(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedWalls,
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedFloors,
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedCeilings,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        FinishBoundaryElementIds directElementIds = new();
        foreach (Face roomFace in roomGeometry.Solid.Faces)
        {
            IList<SpatialElementBoundarySubface> subfaces;
            try
            {
                subfaces = roomGeometry.Results.GetBoundaryFaceInfo(roomFace);
            }
            catch (Exception exception)
            {
                AddWarning(
                    warnings,
                    warningKeys,
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.RoomGeometryUnavailable,
                        $"Не удалось прочитать boundary subfaces помещения {roomId}: {exception.Message}",
                        RoomId: roomId));
                continue;
            }

            foreach (SpatialElementBoundarySubface subface in subfaces)
            {
                FinishPreviewCategory? category = null;
                long? hostElementId = null;
                try
                {
                    category = subface.SubfaceType switch
                    {
                        SubfaceType.Side => FinishPreviewCategory.Walls,
                        SubfaceType.Bottom => FinishPreviewCategory.Floors,
                        SubfaceType.Top => FinishPreviewCategory.Ceilings,
                        _ => null
                    };
                    if (!category.HasValue)
                    {
                        continue;
                    }

                    hostElementId = RevitElementIds.GetValue(
                        subface.SpatialBoundaryElement.HostElementId);
                    IReadOnlyDictionary<long, FinishClassifiedElement> classified = category.Value switch
                    {
                        FinishPreviewCategory.Walls => classifiedWalls,
                        FinishPreviewCategory.Floors => classifiedFloors,
                        FinishPreviewCategory.Ceilings => classifiedCeilings,
                        _ => throw new ArgumentOutOfRangeException(nameof(category))
                    };
                    if (!classified.ContainsKey(hostElementId.Value))
                    {
                        continue;
                    }

                    double areaSquareMeters = RevitAreaUnits.ToSquareMeters(subface.GetSubface().Area);
                    if (areaSquareMeters <= MinimumAreaSquareMeters)
                    {
                        continue;
                    }

                    accumulator.Add(
                        roomId,
                        hostElementId.Value,
                        category.Value,
                        areaSquareMeters,
                        FinishQuantityMethod.RoomBoundarySubface);
                    directElementIds.For(category.Value).Add(hostElementId.Value);
                }
                catch (Exception exception)
                {
                    AddWarning(
                        warnings,
                        warningKeys,
                        new FinishGeometryWarning(
                            FinishGeometryWarningCode.ProjectedAreaUnavailable,
                            $"Не удалось прочитать площадь boundary subface помещения {roomId}"
                                + $"{(hostElementId.HasValue ? $" и элемента {hostElementId.Value}" : string.Empty)}: {exception.Message}",
                            RoomId: roomId,
                            ElementId: hostElementId,
                            Category: category));
                }
            }
        }

        return directElementIds;
    }

    private static void CalculateFallbackWalls(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IEnumerable<FinishClassifiedElement> candidates,
        ISet<long> directWallIds,
        FinishElementGeometryCache geometryCache,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        IReadOnlyList<WallFaceProbe> probes = CreateWallProbes(
            roomId,
            roomGeometry.Solid,
            warnings,
            warningKeys);
        if (probes.Count == 0)
        {
            return;
        }

        foreach (FinishClassifiedElement candidate in candidates
                     .Where(element => element.Category == FinishPreviewCategory.Walls))
        {
            long elementId = candidate.Element.ElementId;
            if (directWallIds.Contains(elementId))
            {
                continue;
            }

            FinishElementGeometryLookup lookup = geometryCache.Get(elementId);
            FinishElementGeometryData? geometry = lookup.Geometry;
            if (lookup.Status != FinishElementGeometryLookupStatus.Success || geometry is null)
            {
                AddMissingGeometryWarning(
                    roomId,
                    elementId,
                    FinishPreviewCategory.Walls,
                    lookup,
                    warnings,
                    warningKeys);
                continue;
            }

            double areaInternal = 0;
            double missingInternal = 0;
            bool hadIntersection = false;
            foreach (Solid elementSolid in geometry.Solids)
            {
                double bestSolidArea = 0;
                foreach (WallFaceProbe probe in probes)
                {
                    if (probe.Bounds is not null && candidate.Element.Bounds is { } elementBounds
                        && !probe.Bounds.Intersects(elementBounds)) continue;
                    bestSolidArea = Math.Max(
                        bestSolidArea,
                        CalculateWallProbeArea(
                            roomId,
                            elementId,
                            elementSolid,
                            probe,
                            warnings,
                            warningKeys,
                            out bool probeIntersected, out double unmeasured));
                    missingInternal += unmeasured;
                    hadIntersection |= probeIntersected;
                }

                areaInternal += bestSolidArea;
            }

            double areaSquareMeters = RevitAreaUnits.ToSquareMeters(areaInternal);
            if (missingInternal > 0)
                AddWarning(warnings, warningKeys, new FinishGeometryWarning(FinishGeometryWarningCode.ProbeCreationFailed,
                    $"Помещение {roomId}, стена {elementId}. Revit не смог измерить часть контакта даже после разбиения границы. Автоматическое назначение для этой стены пропущено. Сверьте её принадлежность в модели.",
                    roomId, elementId, FinishPreviewCategory.Walls)
                    { UnmeasuredWallContactLimit = RevitAreaUnits.ToSquareMeters(missingInternal) });
            if (areaSquareMeters > MinimumAreaSquareMeters)
            {
                accumulator.Add(
                    roomId,
                    elementId,
                    FinishPreviewCategory.Walls,
                    areaSquareMeters,
                    FinishQuantityMethod.WallProbeIntersection);
            }
            else if (hadIntersection)
            {
                AddWarning(
                    warnings,
                    warningKeys,
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.WallFallbackUnresolved,
                        $"Стена {elementId} пересекает probe помещения {roomId}, но надёжная площадь контакта не определена.",
                        RoomId: roomId,
                        ElementId: elementId,
                        Category: FinishPreviewCategory.Walls));
            }
        }
    }

    private static double CalculateWallProbeArea(
        long roomId,
        long elementId,
        Solid elementSolid,
        WallFaceProbe probe,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys,
        out bool hadIntersection, out double unmeasured)
    {
        double missing = 0;
        double outward = MeasureSide(probe.Outward, out bool outwardHit);
        double inward = MeasureSide(probe.Inward, out bool inwardHit);
        unmeasured = missing;
        hadIntersection = outwardHit || inwardHit;
        return Math.Max(outward, inward);

        double MeasureSide(IReadOnlyList<Solid> fragments, out bool intersects)
        {
            intersects = false;
            double total = 0;
            foreach (Solid fragment in fragments)
            {
                using Solid? intersection = TryIntersect(roomId, elementId, FinishPreviewCategory.Walls,
                    fragment, elementSolid, warnings, warningKeys);
                if (intersection is null && probe.IsMesh)
                {
                    // A parallel contact patch inside this prism projects into its triangular base.
                    missing += fragment.Volume / (ProbeThickness * ParallelWallNormalTolerance);
                }
                if (intersection is null || intersection.Volume <= MinimumIntersectionVolume) continue;

                intersects = true;
                total += FinishGeometryAreaRules.SelectParallelFaceArea(GetFaceMeasures(intersection),
                    probe.Normal.X, probe.Normal.Y, probe.Normal.Z, ParallelWallNormalTolerance) ?? 0;
            }
            return total;
        }
    }

    private static void CalculateSlabs(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IEnumerable<FinishClassifiedElement> candidates,
        FinishPreviewCategory category,
        ISet<long> directSlabIds,
        FinishElementGeometryCache geometryCache,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        if (category == FinishPreviewCategory.Ceilings)
        {
            CalculateCeilings(
                roomId,
                roomGeometry,
                candidates,
                directSlabIds,
                geometryCache,
                accumulator,
                warnings,
                warningKeys);
            return;
        }

        IReadOnlyList<FinishSolidProbe> probes = CreateHorizontalProbes(
            roomId,
            roomGeometry.Solid,
            category,
            warnings,
            warningKeys);
        if (probes.Count == 0)
        {
            return;
        }

        foreach (FinishClassifiedElement candidate in candidates.Where(element => element.Category == category))
        {
            long elementId = candidate.Element.ElementId;
            if (directSlabIds.Contains(elementId))
            {
                continue;
            }

            FinishElementGeometryLookup lookup = geometryCache.Get(elementId);
            FinishElementGeometryData? geometry = lookup.Geometry;
            if (lookup.Status != FinishElementGeometryLookupStatus.Success || geometry is null)
            {
                AddMissingGeometryWarning(
                    roomId,
                    elementId,
                    category,
                    lookup,
                    warnings,
                    warningKeys);
                continue;
            }

            IReadOnlyList<Solid> supportedSolids = geometry.Solids
                .Where(solid => FinishGeometryAreaRules.HasOpposingHorizontalFaces(
                    GetFaceMeasures(solid),
                    HorizontalNormalTolerance))
                .ToArray();
            if (supportedSolids.Count == 0)
            {
                AddWarning(
                    warnings,
                    warningKeys,
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.SlabGeometryUnsupported,
                        $"Перекрытие пола {elementId} не имеет пары противоположных горизонтальных граней и пропущено.",
                        RoomId: roomId,
                        ElementId: elementId,
                        Category: category));
                continue;
            }

            double areaInternal = 0;
            bool hadIntersection = false;
            foreach (FinishSolidProbe probe in probes)
            {
                foreach (Solid elementSolid in supportedSolids)
                {
                    FinishProbeIntersectionResult intersectionResult = IntersectProbe(
                        probe,
                        elementSolid);
                    bool measuredIntersection = false;
                    foreach (Solid intersection in intersectionResult.Intersections)
                    {
                        if (intersection.Volume <= MinimumIntersectionVolume)
                        {
                            continue;
                        }

                        hadIntersection = true;
                        measuredIntersection = true;
                        double? projectedArea = FinishGeometryAreaRules.SelectHorizontalProjectedArea(
                            GetFaceMeasures(intersection));
                        if (projectedArea.HasValue)
                        {
                            areaInternal += projectedArea.Value;
                        }
                    }

                    if (!measuredIntersection && intersectionResult.Failure is not null)
                    {
                        AddBooleanIntersectionWarning(
                            roomId,
                            elementId,
                            category,
                            intersectionResult.Failure,
                            warnings,
                            warningKeys);
                    }
                }
            }

            double areaSquareMeters = RevitAreaUnits.ToSquareMeters(areaInternal);
            if (areaSquareMeters > MinimumAreaSquareMeters)
            {
                accumulator.Add(
                    roomId,
                    elementId,
                    category,
                    areaSquareMeters,
                    FinishQuantityMethod.FloorProbeIntersection);
            }
            else if (hadIntersection)
            {
                AddWarning(
                    warnings,
                    warningKeys,
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.ProjectedAreaUnavailable,
                        $"Пересечение перекрытия {elementId} с помещением {roomId} найдено, но горизонтальная площадь не определена.",
                        RoomId: roomId,
                        ElementId: elementId,
                        Category: category));
            }
        }
    }

    private static void CalculateCeilings(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IEnumerable<FinishClassifiedElement> candidates,
        ISet<long> directCeilingIds,
        FinishElementGeometryCache geometryCache,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        foreach (FinishClassifiedElement candidate in candidates
                     .Where(element => element.Category == FinishPreviewCategory.Ceilings))
        {
            long elementId = candidate.Element.ElementId;
            if (directCeilingIds.Contains(elementId))
            {
                continue;
            }

            FinishElementGeometryLookup lookup = geometryCache.Get(elementId);
            FinishElementGeometryData? geometry = lookup.Geometry;
            if (lookup.Status != FinishElementGeometryLookupStatus.Success || geometry is null)
            {
                AddMissingGeometryWarning(
                    roomId,
                    elementId,
                    FinishPreviewCategory.Ceilings,
                    lookup,
                    warnings,
                    warningKeys);
                continue;
            }

            AxisAlignedBox3D? elementBounds = candidate.Element.Bounds;
            if (elementBounds is null)
            {
                continue;
            }

            IReadOnlyList<FinishSolidProbe> roomColumns = CreateVerticalRoomColumns(
                roomId,
                elementId,
                roomGeometry.Solid,
                elementBounds.MaxZ + ProbeThickness,
                warnings,
                warningKeys);
            if (roomColumns.Count == 0)
            {
                continue;
            }

            double areaInternal = 0;
            bool hadIntersection = false;
            foreach (FinishSolidProbe roomColumn in roomColumns)
            {
                foreach (Solid elementSolid in geometry.Solids)
                {
                    FinishProbeIntersectionResult intersectionResult = IntersectProbe(
                        roomColumn,
                        elementSolid);
                    bool measuredIntersection = false;
                    foreach (Solid intersection in intersectionResult.Intersections)
                    {
                        if (intersection.Volume <= MinimumIntersectionVolume)
                        {
                            continue;
                        }

                        hadIntersection = true;
                        measuredIntersection = true;
                        double? contactArea = FinishGeometryAreaRules.SumDownwardFacingArea(
                            GetFaceMeasures(intersection),
                            MinimumCeilingVerticalNormalComponent);
                        if (contactArea.HasValue)
                        {
                            areaInternal += contactArea.Value;
                        }
                    }

                    if (!measuredIntersection && intersectionResult.Failure is not null)
                    {
                        AddBooleanIntersectionWarning(
                            roomId,
                            elementId,
                            FinishPreviewCategory.Ceilings,
                            intersectionResult.Failure,
                            warnings,
                            warningKeys);
                    }
                }
            }

            double areaSquareMeters = RevitAreaUnits.ToSquareMeters(areaInternal);
            if (areaSquareMeters > MinimumAreaSquareMeters)
            {
                accumulator.Add(
                    roomId,
                    elementId,
                    FinishPreviewCategory.Ceilings,
                    areaSquareMeters,
                    FinishQuantityMethod.CeilingProbeIntersection);
            }
            else if (hadIntersection)
            {
                AddWarning(
                    warnings,
                    warningKeys,
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.ProjectedAreaUnavailable,
                        $"Потолок {elementId} пересекает вертикальный контур помещения {roomId}, но площадь нижней поверхности не определена.",
                        RoomId: roomId,
                        ElementId: elementId,
                        Category: FinishPreviewCategory.Ceilings));
            }
        }
    }

    private static IReadOnlyList<WallFaceProbe> CreateWallProbes(
        long roomId,
        Solid roomSolid,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        List<WallFaceProbe> probes = [];
        foreach (PlanarFace face in roomSolid.Faces
                     .OfType<PlanarFace>()
                     .Where(face => Math.Abs(face.FaceNormal.Z) <= VerticalNormalTolerance))
        {
            AxisAlignedBox3D? bounds = GetFaceProbeBounds(face);
            try
            {
                XYZ normal = face.FaceNormal.Normalize();
                IList<CurveLoop> loops = face.GetEdgesAsCurveLoops();
                Solid outward = GeometryCreationUtilities.CreateExtrusionGeometry(
                    loops,
                    normal,
                    ProbeThickness);
                Solid inward = GeometryCreationUtilities.CreateExtrusionGeometry(
                    loops,
                    normal.Negate(),
                    ProbeThickness);
                probes.Add(new WallFaceProbe(normal, [outward], [inward], bounds));
            }
            catch (Exception exception)
            {
                // A trimmed face can be valid even when its consecutive edge endpoints disagree.
                // Revit's face mesh preserves the opening boundaries without joining those endpoints.
                WallFaceProbe? recovered = TryCreateWallMeshProbe(face, bounds);
                if (recovered is not null)
                {
                    probes.Add(recovered);
                    AddWarning(warnings, warningKeys, new FinishGeometryWarning(
                        FinishGeometryWarningCode.WallFaceRecovered,
                        $"Помещение {roomId}. Revit не смог проверить одну из границ стен целиком. Плагин проверил эту поверхность по частям. Исправлять контур из-за этого сообщения не требуется.",
                        RoomId: roomId, Category: FinishPreviewCategory.Walls));
                    continue;
                }
                AddProbeWarning(
                    roomId,
                    FinishPreviewCategory.Walls,
                    exception,
                    warnings,
                    warningKeys,
                    bounds: bounds);
            }
        }

        return probes;
    }

    private static WallFaceProbe? TryCreateWallMeshProbe(PlanarFace face, AxisAlignedBox3D? bounds)
    {
        try
        {
            XYZ normal = face.FaceNormal.Normalize();
            var outward = CreateTriangularExtrusions(face, normal, ProbeThickness, requireComplete: true);
            var inward = CreateTriangularExtrusions(face, -normal, ProbeThickness, requireComplete: true);
            double area = face.Area;
            // Reject an incomplete mesh or one whose projected area differs by more than 0.1%.
            bool CoversFace(IReadOnlyList<Solid> fragments) => fragments.Count > 0 && area > 0
                && Math.Abs(fragments.Sum(fragment => fragment.Volume) / ProbeThickness - area) <= area * 0.001;
            return CoversFace(outward) && CoversFace(inward) ? new WallFaceProbe(normal, outward, inward, bounds, IsMesh: true) : null;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    private static IReadOnlyList<FinishSolidProbe> CreateHorizontalProbes(
        long roomId,
        Solid roomSolid,
        FinishPreviewCategory category,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        bool floor = category == FinishPreviewCategory.Floors;
        List<FinishSolidProbe> probes = [];
        IEnumerable<PlanarFace> faces = roomSolid.Faces
            .OfType<PlanarFace>()
            .Where(face => floor
                ? face.FaceNormal.Z <= -HorizontalNormalTolerance
                : face.FaceNormal.Z >= HorizontalNormalTolerance);
        foreach (PlanarFace face in faces)
        {
            try
            {
                XYZ direction = face.FaceNormal.Normalize();
                Solid primary = GeometryCreationUtilities.CreateExtrusionGeometry(
                    face.GetEdgesAsCurveLoops(),
                    direction,
                    ProbeThickness);
                probes.Add(new FinishSolidProbe(
                    primary,
                    () => CreateTriangularExtrusions(face, direction, ProbeThickness)));
            }
            catch (Exception exception)
            {
                AddProbeWarning(roomId, category, exception, warnings, warningKeys, bounds: GetFaceProbeBounds(face));
            }
        }

        return probes;
    }

    private static IReadOnlyList<FinishSolidProbe> CreateVerticalRoomColumns(
        long roomId,
        long elementId,
        Solid roomSolid,
        double topZ,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        List<FinishSolidProbe> columns = [];
        IEnumerable<PlanarFace> bottomFaces = roomSolid.Faces
            .OfType<PlanarFace>()
            .Where(face => face.FaceNormal.Z <= -HorizontalNormalTolerance);
        foreach (PlanarFace face in bottomFaces)
        {
            double height = topZ - face.Origin.Z;
            if (height <= ProbeThickness)
            {
                continue;
            }

            try
            {
                Solid primary = GeometryCreationUtilities.CreateExtrusionGeometry(
                    face.GetEdgesAsCurveLoops(),
                    XYZ.BasisZ,
                    height);
                columns.Add(new FinishSolidProbe(
                    primary,
                    () => CreateTriangularExtrusions(face, XYZ.BasisZ, height)));
            }
            catch (Exception exception)
            {
                AddProbeWarning(
                    roomId,
                    FinishPreviewCategory.Ceilings,
                    exception,
                    warnings,
                    warningKeys,
                    elementId: elementId);
            }
        }

        return columns;
    }

    private static IReadOnlyList<Solid> CreateTriangularExtrusions(
        Face face,
        XYZ direction,
        double distance,
        bool requireComplete = false)
    {
        List<Solid> fragments = [];
        Mesh mesh;
        try
        {
            mesh = face.Triangulate();
        }
        catch
        {
            return fragments;
        }

        for (int index = 0; index < mesh.NumTriangles; index++)
        {
            try
            {
                MeshTriangle triangle = mesh.get_Triangle(index);
                XYZ first = triangle.get_Vertex(0);
                XYZ second = triangle.get_Vertex(1);
                XYZ third = triangle.get_Vertex(2);
                if (second.Subtract(first).CrossProduct(third.Subtract(first)).GetLength() <= 1e-9)
                {
                    continue;
                }

                CurveLoop loop = new();
                loop.Append(Line.CreateBound(first, second));
                loop.Append(Line.CreateBound(second, third));
                loop.Append(Line.CreateBound(third, first));
                fragments.Add(GeometryCreationUtilities.CreateExtrusionGeometry(
                    [loop],
                    direction,
                    distance));
            }
            catch
            {
                if (requireComplete) return [];
                // The primary probe remains valid when an optional recovery fragment cannot be built.
            }
        }

        return fragments;
    }

    private static FinishProbeIntersectionResult IntersectProbe(
        FinishSolidProbe probe,
        Solid elementSolid)
    {
        if (TryExecuteIntersection(
                probe.Primary,
                elementSolid,
                out Solid? primary,
                out Exception? primaryFailure))
        {
            return new FinishProbeIntersectionResult([primary!], null);
        }

        List<Solid> recovered = [];
        foreach (Solid fragment in probe.GetFragments())
        {
            if (TryExecuteIntersection(
                    fragment,
                    elementSolid,
                    out Solid? intersection,
                    out _))
            {
                recovered.Add(intersection!);
            }
        }

        return new FinishProbeIntersectionResult(recovered, primaryFailure);
    }

    private static Solid? TryIntersect(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        Solid first,
        Solid second,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        if (TryExecuteIntersection(first, second, out Solid? intersection, out Exception? failure))
        {
            return intersection;
        }

        AddBooleanIntersectionWarning(
            roomId,
            elementId,
            category,
            failure!,
            warnings,
            warningKeys);
        return null;
    }

    private static bool TryExecuteIntersection(
        Solid first,
        Solid second,
        out Solid? intersection,
        out Exception? failure)
    {
        try
        {
            intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                first,
                second,
                BooleanOperationsType.Intersect);
            failure = null;
            return true;
        }
        catch (Exception firstFailure)
        {
            try
            {
                intersection = BooleanOperationsUtils.ExecuteBooleanOperation(
                    second,
                    first,
                    BooleanOperationsType.Intersect);
                failure = null;
                return true;
            }
            catch (Exception secondFailure)
            {
                intersection = null;
                failure = new InvalidOperationException(
                    $"Прямой расчёт: {firstFailure.Message} Обратный расчёт: {secondFailure.Message}",
                    secondFailure);
                return false;
            }
        }
    }

    private static void AddBooleanIntersectionWarning(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        Exception exception,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        AddWarning(
            warnings,
            warningKeys,
            new FinishGeometryWarning(
                FinishGeometryWarningCode.BooleanIntersectionFailed,
                $"Помещение {roomId}, элемент {elementId}. Revit не смог точно измерить одно из пересечений. Плагин продолжил расчёт по остальным граням и доступным контактам.",
                RoomId: roomId,
                ElementId: elementId,
                Category: category) { TechnicalDetail = exception.Message });
    }

    private static IReadOnlyList<FinishFaceMeasure> GetFaceMeasures(Solid solid)
    {
        List<FinishFaceMeasure> measures = [];
        foreach (Face face in solid.Faces)
        {
            try
            {
                XYZ normal;
                if (face is PlanarFace planarFace)
                {
                    normal = planarFace.FaceNormal;
                }
                else
                {
                    BoundingBoxUV bounds = face.GetBoundingBox();
                    UV midpoint = new(
                        (bounds.Min.U + bounds.Max.U) / 2,
                        (bounds.Min.V + bounds.Max.V) / 2);
                    normal = face.ComputeNormal(midpoint);
                }

                measures.Add(new FinishFaceMeasure(
                    face.Area,
                    normal.X,
                    normal.Y,
                    normal.Z));
            }
            catch
            {
                // A single unreadable face must not discard other measurable faces of the intersection.
            }
        }

        return measures;
    }

    private static void AddMissingGeometryWarning(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        FinishElementGeometryLookup lookup,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys)
    {
        AddWarning(
            warnings,
            warningKeys,
            new FinishGeometryWarning(
                lookup.Status == FinishElementGeometryLookupStatus.ElementNotFound
                    ? FinishGeometryWarningCode.ElementNotFound
                    : FinishGeometryWarningCode.ElementGeometryUnavailable,
                $"Геометрия элемента отделки {elementId} недоступна ({lookup.Details}); элемент пропущен.",
                RoomId: roomId,
                ElementId: elementId,
                Category: category));
    }

    private static void AddProbeWarning(
        long roomId,
        FinishPreviewCategory category,
        Exception exception,
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys,
        AxisAlignedBox3D? bounds = null,
        long? elementId = null)
    {
        AddWarning(
            warnings,
            warningKeys,
            new FinishGeometryWarning(
                FinishGeometryWarningCode.ProbeCreationFailed,
                $"Помещение {roomId}. Revit не смог построить проверочный объём вдоль границы для {FinishCategoryName(category)}. Принадлежность отделки возле этого участка осталась неподтверждённой. Проверьте её в модели перед формированием ведомости.",
                RoomId: roomId,
                ElementId: elementId,
                Category: category)
            { ProbeBounds = bounds, TechnicalDetail = exception.Message });
    }

    private static void AddWarning(
        List<FinishGeometryWarning> warnings,
        HashSet<FinishGeometryWarning> warningKeys,
        FinishGeometryWarning warning)
    {
        if (warningKeys.Add(warning))
        {
            warnings.Add(warning);
        }
    }

    private static AxisAlignedBox3D? GetFaceProbeBounds(PlanarFace face)
    {
        // A planar UV rectangle encloses the trimmed face, including curved edges.
        // Pad all axes to cover both directions of extrusion and numerical roundoff.
        try
        {
            using BoundingBoxUV uv = face.GetBoundingBox();
            XYZ[] corners = [face.Evaluate(uv.Min), face.Evaluate(new UV(uv.Min.U, uv.Max.V)),
                face.Evaluate(uv.Max), face.Evaluate(new UV(uv.Max.U, uv.Min.V))];
            const double padding = ProbeThickness + 1e-6;
            return new AxisAlignedBox3D(corners.Min(p => p.X) - padding, corners.Min(p => p.Y) - padding,
                corners.Min(p => p.Z) - padding, corners.Max(p => p.X) + padding,
                corners.Max(p => p.Y) + padding, corners.Max(p => p.Z) + padding);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return null;
        }
    }

    private static string FinishCategoryName(FinishPreviewCategory category) => category switch
    {
        FinishPreviewCategory.Walls => "стен",
        FinishPreviewCategory.Floors => "полов",
        _ => "потолков"
    };

    private sealed record WallFaceProbe(XYZ Normal, IReadOnlyList<Solid> Outward, IReadOnlyList<Solid> Inward,
        AxisAlignedBox3D? Bounds, bool IsMesh = false);

    private sealed class FinishSolidProbe
    {
        private readonly Func<IReadOnlyList<Solid>> fragmentFactory;
        private IReadOnlyList<Solid>? fragments;

        public FinishSolidProbe(
            Solid primary,
            Func<IReadOnlyList<Solid>> fragmentFactory)
        {
            Primary = primary ?? throw new ArgumentNullException(nameof(primary));
            this.fragmentFactory = fragmentFactory
                ?? throw new ArgumentNullException(nameof(fragmentFactory));
        }

        public Solid Primary { get; }

        public IReadOnlyList<Solid> GetFragments()
        {
            return fragments ??= fragmentFactory();
        }
    }

    private sealed record FinishProbeIntersectionResult(
        IReadOnlyList<Solid> Intersections,
        Exception? Failure);

    private sealed class FinishBoundaryElementIds
    {
        public HashSet<long> Walls { get; } = [];

        public HashSet<long> Floors { get; } = [];

        public HashSet<long> Ceilings { get; } = [];

        public HashSet<long> For(FinishPreviewCategory category)
        {
            return category switch
            {
                FinishPreviewCategory.Walls => Walls,
                FinishPreviewCategory.Floors => Floors,
                FinishPreviewCategory.Ceilings => Ceilings,
                _ => throw new ArgumentOutOfRangeException(nameof(category))
            };
        }
    }
}
