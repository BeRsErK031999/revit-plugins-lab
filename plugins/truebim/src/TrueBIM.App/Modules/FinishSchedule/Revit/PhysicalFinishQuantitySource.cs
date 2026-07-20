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
    private const double MinimumIntersectionVolume = 1e-10;
    private const double MinimumAreaSquareMeters = 1e-8;

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
        HashSet<string> warningKeys = new(StringComparer.Ordinal);
        FinishBoundingBoxIndex index = new(request.Elements);
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedWalls = request.Elements
            .Where(element => element.Category == FinishPreviewCategory.Walls)
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

            HashSet<long> directWallIds = CalculateBoundaryWalls(
                room.ElementId,
                roomGeometry,
                classifiedWalls,
                accumulator,
                warnings,
                warningKeys);
            IReadOnlyList<FinishClassifiedElement> candidates = room.Bounds is null
                ? []
                : index.Query(room.Bounds, ProbeThickness);
            CalculateFallbackWalls(
                room.ElementId,
                roomGeometry,
                candidates,
                directWallIds,
                elementGeometryCache,
                accumulator,
                warnings,
                warningKeys);
            CalculateSlabs(
                room.ElementId,
                roomGeometry,
                candidates,
                FinishPreviewCategory.Floors,
                elementGeometryCache,
                accumulator,
                warnings,
                warningKeys);
            CalculateSlabs(
                room.ElementId,
                roomGeometry,
                candidates,
                FinishPreviewCategory.Ceilings,
                elementGeometryCache,
                accumulator,
                warnings,
                warningKeys);
        }

        FinishQuantityResult result = new(
            accumulator.Build(),
            warnings,
            new FinishGeometryCacheMetrics(
                roomGeometryCache.RequestCount,
                roomGeometryCache.EntryCount,
                roomGeometryCache.HitCount,
                elementGeometryCache.RequestCount,
                elementGeometryCache.EntryCount,
                elementGeometryCache.HitCount));
        logger.Info(
            $"Finish Schedule physical quantities calculated. Rooms={request.Rooms.Count}; "
            + $"Occurrences={result.Occurrences.Count}; Warnings={result.Warnings.Count}; "
            + $"RoomCache={roomGeometryCache.EntryCount}/{roomGeometryCache.RequestCount}; "
            + $"ElementCache={elementGeometryCache.EntryCount}/{elementGeometryCache.RequestCount}; "
            + $"ElementCacheHits={elementGeometryCache.HitCount}.");
        return result;
    }

    private static HashSet<long> CalculateBoundaryWalls(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IReadOnlyDictionary<long, FinishClassifiedElement> classifiedWalls,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
    {
        HashSet<long> directWallIds = [];
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
                        RoomId: roomId,
                        Category: FinishPreviewCategory.Walls));
                continue;
            }

            foreach (SpatialElementBoundarySubface subface in subfaces)
            {
                try
                {
                    if (subface.SubfaceType != SubfaceType.Side)
                    {
                        continue;
                    }

                    long hostElementId = RevitElementIds.GetValue(
                        subface.SpatialBoundaryElement.HostElementId);
                    if (!classifiedWalls.ContainsKey(hostElementId))
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
                        hostElementId,
                        FinishPreviewCategory.Walls,
                        areaSquareMeters,
                        FinishQuantityMethod.RoomBoundarySubface);
                    directWallIds.Add(hostElementId);
                }
                catch (Exception exception)
                {
                    AddWarning(
                        warnings,
                        warningKeys,
                        new FinishGeometryWarning(
                            FinishGeometryWarningCode.ProjectedAreaUnavailable,
                            $"Не удалось прочитать площадь boundary subface помещения {roomId}: {exception.Message}",
                            RoomId: roomId,
                            Category: FinishPreviewCategory.Walls));
                }
            }
        }

        return directWallIds;
    }

    private static void CalculateFallbackWalls(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IEnumerable<FinishClassifiedElement> candidates,
        ISet<long> directWallIds,
        FinishElementGeometryCache geometryCache,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
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
            bool hadIntersection = false;
            foreach (Solid elementSolid in geometry.Solids)
            {
                double bestSolidArea = 0;
                foreach (WallFaceProbe probe in probes)
                {
                    bestSolidArea = Math.Max(
                        bestSolidArea,
                        CalculateWallProbeArea(
                            roomId,
                            elementId,
                            elementSolid,
                            probe,
                            warnings,
                            warningKeys,
                            out bool probeIntersected));
                    hadIntersection |= probeIntersected;
                }

                areaInternal += bestSolidArea;
            }

            double areaSquareMeters = RevitAreaUnits.ToSquareMeters(areaInternal);
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
        HashSet<string> warningKeys,
        out bool hadIntersection)
    {
        hadIntersection = false;
        double bestArea = 0;
        foreach (Solid probeSolid in probe.Solids)
        {
            Solid? intersection = TryIntersect(
                roomId,
                elementId,
                FinishPreviewCategory.Walls,
                probeSolid,
                elementSolid,
                warnings,
                warningKeys);
            if (intersection is null || intersection.Volume <= MinimumIntersectionVolume)
            {
                continue;
            }

            hadIntersection = true;
            double? area = FinishGeometryAreaRules.SelectParallelFaceArea(
                GetFaceMeasures(intersection),
                probe.Normal.X,
                probe.Normal.Y,
                probe.Normal.Z);
            if (area.HasValue)
            {
                bestArea = Math.Max(bestArea, area.Value);
            }
        }

        return bestArea;
    }

    private static void CalculateSlabs(
        long roomId,
        FinishRoomGeometryData roomGeometry,
        IEnumerable<FinishClassifiedElement> candidates,
        FinishPreviewCategory category,
        FinishElementGeometryCache geometryCache,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
    {
        IReadOnlyList<Solid> probes = CreateHorizontalProbes(
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
                string elementKind = category == FinishPreviewCategory.Ceilings
                    ? "Потолок"
                    : "Перекрытие пола";
                AddWarning(
                    warnings,
                    warningKeys,
                    new FinishGeometryWarning(
                        FinishGeometryWarningCode.SlabGeometryUnsupported,
                        $"{elementKind} {elementId} не имеет пары противоположных горизонтальных граней и пропущен.",
                        RoomId: roomId,
                        ElementId: elementId,
                        Category: category));
                continue;
            }

            double areaInternal = 0;
            bool hadIntersection = false;
            foreach (Solid probe in probes)
            {
                foreach (Solid elementSolid in supportedSolids)
                {
                    Solid? intersection = TryIntersect(
                        roomId,
                        elementId,
                        category,
                        probe,
                        elementSolid,
                        warnings,
                        warningKeys);
                    if (intersection is null || intersection.Volume <= MinimumIntersectionVolume)
                    {
                        continue;
                    }

                    hadIntersection = true;
                    double? projectedArea = FinishGeometryAreaRules.SelectHorizontalProjectedArea(
                        GetFaceMeasures(intersection));
                    if (projectedArea.HasValue)
                    {
                        areaInternal += projectedArea.Value;
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
                    category == FinishPreviewCategory.Floors
                        ? FinishQuantityMethod.FloorProbeIntersection
                        : FinishQuantityMethod.CeilingProbeIntersection);
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

    private static IReadOnlyList<WallFaceProbe> CreateWallProbes(
        long roomId,
        Solid roomSolid,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
    {
        List<WallFaceProbe> probes = [];
        foreach (PlanarFace face in roomSolid.Faces
                     .OfType<PlanarFace>()
                     .Where(face => Math.Abs(face.FaceNormal.Z) <= VerticalNormalTolerance))
        {
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
                probes.Add(new WallFaceProbe(normal, [outward, inward]));
            }
            catch (Exception exception)
            {
                AddProbeWarning(
                    roomId,
                    FinishPreviewCategory.Walls,
                    exception,
                    warnings,
                    warningKeys);
            }
        }

        return probes;
    }

    private static IReadOnlyList<Solid> CreateHorizontalProbes(
        long roomId,
        Solid roomSolid,
        FinishPreviewCategory category,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
    {
        bool floor = category == FinishPreviewCategory.Floors;
        List<Solid> probes = [];
        IEnumerable<PlanarFace> faces = roomSolid.Faces
            .OfType<PlanarFace>()
            .Where(face => floor
                ? face.FaceNormal.Z <= -HorizontalNormalTolerance
                : face.FaceNormal.Z >= HorizontalNormalTolerance);
        foreach (PlanarFace face in faces)
        {
            try
            {
                probes.Add(GeometryCreationUtilities.CreateExtrusionGeometry(
                    face.GetEdgesAsCurveLoops(),
                    face.FaceNormal.Normalize(),
                    ProbeThickness));
            }
            catch (Exception exception)
            {
                AddProbeWarning(roomId, category, exception, warnings, warningKeys);
            }
        }

        return probes;
    }

    private static Solid? TryIntersect(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        Solid first,
        Solid second,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
    {
        try
        {
            return BooleanOperationsUtils.ExecuteBooleanOperation(
                first,
                second,
                BooleanOperationsType.Intersect);
        }
        catch (Exception exception)
        {
            AddWarning(
                warnings,
                warningKeys,
                new FinishGeometryWarning(
                    FinishGeometryWarningCode.BooleanIntersectionFailed,
                    $"Не удалось пересечь геометрию помещения {roomId} и элемента {elementId}: {exception.Message}",
                    RoomId: roomId,
                    ElementId: elementId,
                    Category: category));
            return null;
        }
    }

    private static IReadOnlyList<FinishFaceMeasure> GetFaceMeasures(Solid solid)
    {
        return solid.Faces
            .OfType<PlanarFace>()
            .Select(face => new FinishFaceMeasure(
                face.Area,
                face.FaceNormal.X,
                face.FaceNormal.Y,
                face.FaceNormal.Z))
            .ToArray();
    }

    private static void AddMissingGeometryWarning(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        FinishElementGeometryLookup lookup,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
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
        HashSet<string> warningKeys)
    {
        AddWarning(
            warnings,
            warningKeys,
            new FinishGeometryWarning(
                FinishGeometryWarningCode.ProbeCreationFailed,
                $"Не удалось построить geometry probe помещения {roomId}: {exception.Message}",
                RoomId: roomId,
                Category: category));
    }

    private static void AddWarning(
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys,
        FinishGeometryWarning warning)
    {
        string key = $"{warning.Code}|{warning.RoomId}|{warning.ElementId}|{warning.Category}|{warning.Message}";
        if (warningKeys.Add(key))
        {
            warnings.Add(warning);
        }
    }

    private sealed record WallFaceProbe(XYZ Normal, IReadOnlyList<Solid> Solids);
}
