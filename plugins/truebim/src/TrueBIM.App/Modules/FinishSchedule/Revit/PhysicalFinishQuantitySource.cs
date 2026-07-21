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
                : QueryCandidates(index, room.Bounds);
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

    private static IReadOnlyList<FinishClassifiedElement> QueryCandidates(
        FinishBoundingBoxIndex index,
        AxisAlignedBox3D roomBounds)
    {
        FinishPreviewCategory[] categories =
        [
            FinishPreviewCategory.Walls,
            FinishPreviewCategory.Floors,
            FinishPreviewCategory.Ceilings
        ];
        return categories
            .SelectMany(category => index.Query(
                    FinishCandidateSearchRules.CreateSearchBounds(roomBounds, category))
                .Where(element => element.Category == category))
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
        HashSet<string> warningKeys)
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
        ISet<long> directSlabIds,
        FinishElementGeometryCache geometryCache,
        FinishOccurrenceAccumulator accumulator,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
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
        HashSet<string> warningKeys)
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

    private static IReadOnlyList<FinishSolidProbe> CreateHorizontalProbes(
        long roomId,
        Solid roomSolid,
        FinishPreviewCategory category,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
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
                AddProbeWarning(roomId, category, exception, warnings, warningKeys);
            }
        }

        return probes;
    }

    private static IReadOnlyList<FinishSolidProbe> CreateVerticalRoomColumns(
        long roomId,
        Solid roomSolid,
        double topZ,
        List<FinishGeometryWarning> warnings,
        HashSet<string> warningKeys)
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
                    warningKeys);
            }
        }

        return columns;
    }

    private static IReadOnlyList<Solid> CreateTriangularExtrusions(
        Face face,
        XYZ direction,
        double distance)
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
        HashSet<string> warningKeys)
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
        HashSet<string> warningKeys)
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
