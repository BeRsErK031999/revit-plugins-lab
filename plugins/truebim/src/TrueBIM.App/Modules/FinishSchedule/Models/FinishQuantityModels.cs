namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum FinishQuantityMethod
{
    RoomBoundarySubface,
    WallProbeIntersection,
    FloorProbeIntersection,
    CeilingProbeIntersection
}

public enum FinishGeometryWarningCode
{
    RoomNotFound,
    RoomGeometryUnavailable,
    ElementNotFound,
    ElementGeometryUnavailable,
    WallFallbackUnresolved,
    SlabGeometryUnsupported,
    ProbeCreationFailed,
    BooleanIntersectionFailed,
    ProjectedAreaUnavailable
}

public sealed record FinishGeometryWarning(
    FinishGeometryWarningCode Code,
    string Message,
    long? RoomId = null,
    long? ElementId = null,
    FinishPreviewCategory? Category = null);

public sealed class FinishQuantityRequest
{
    public FinishQuantityRequest(
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        IEnumerable<FinishClassifiedElement> elements)
    {
        Rooms = (rooms ?? throw new ArgumentNullException(nameof(rooms)))
            .GroupBy(room => room.ElementId)
            .Select(group => group.First())
            .OrderBy(room => room.ElementId)
            .ToArray();
        Elements = (elements ?? throw new ArgumentNullException(nameof(elements)))
            .GroupBy(element => new { element.Element.ElementId, element.Category })
            .Select(group => group.First())
            .OrderBy(element => element.Element.ElementId)
            .ThenBy(element => element.Category)
            .ToArray();
    }

    public IReadOnlyList<FinishRoomCandidateSnapshot> Rooms { get; }

    public IReadOnlyList<FinishClassifiedElement> Elements { get; }
}

public sealed record FinishOccurrence
{
    public FinishOccurrence(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        double areaSquareMeters,
        FinishQuantityMethod method)
    {
        if (roomId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(roomId));
        }

        if (elementId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementId));
        }

        if (areaSquareMeters <= 0
            || double.IsNaN(areaSquareMeters)
            || double.IsInfinity(areaSquareMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(areaSquareMeters));
        }

        RoomId = roomId;
        ElementId = elementId;
        Category = category;
        AreaSquareMeters = areaSquareMeters;
        Method = method;
    }

    public long RoomId { get; }

    public long ElementId { get; }

    public FinishPreviewCategory Category { get; }

    public double AreaSquareMeters { get; }

    public FinishQuantityMethod Method { get; }
}

public sealed class FinishQuantityResult
{
    public FinishQuantityResult(
        IEnumerable<FinishOccurrence> occurrences,
        IEnumerable<FinishGeometryWarning> warnings)
    {
        Occurrences = (occurrences ?? throw new ArgumentNullException(nameof(occurrences)))
            .OrderBy(occurrence => occurrence.RoomId)
            .ThenBy(occurrence => occurrence.Category)
            .ThenBy(occurrence => occurrence.ElementId)
            .ToArray();
        Warnings = (warnings ?? throw new ArgumentNullException(nameof(warnings)))
            .OrderBy(warning => warning.RoomId ?? long.MaxValue)
            .ThenBy(warning => warning.ElementId ?? long.MaxValue)
            .ThenBy(warning => warning.Code)
            .ToArray();
        Summary = FinishQuantityPreviewSummary.Create(Occurrences);
    }

    public IReadOnlyList<FinishOccurrence> Occurrences { get; }

    public IReadOnlyList<FinishGeometryWarning> Warnings { get; }

    public FinishQuantityPreviewSummary Summary { get; }
}

public sealed record FinishQuantityCategorySummary(
    int OccurrenceCount,
    int RoomCount,
    int ElementCount,
    double AreaSquareMeters);

public sealed class FinishQuantityPreviewSummary
{
    public FinishQuantityPreviewSummary(
        FinishQuantityCategorySummary walls,
        FinishQuantityCategorySummary floors,
        FinishQuantityCategorySummary ceilings)
    {
        Walls = walls ?? throw new ArgumentNullException(nameof(walls));
        Floors = floors ?? throw new ArgumentNullException(nameof(floors));
        Ceilings = ceilings ?? throw new ArgumentNullException(nameof(ceilings));
    }

    public FinishQuantityCategorySummary Walls { get; }

    public FinishQuantityCategorySummary Floors { get; }

    public FinishQuantityCategorySummary Ceilings { get; }

    public static FinishQuantityPreviewSummary Create(IEnumerable<FinishOccurrence> occurrences)
    {
        FinishOccurrence[] all = (occurrences ?? throw new ArgumentNullException(nameof(occurrences)))
            .ToArray();
        return new FinishQuantityPreviewSummary(
            CreateCategory(all, FinishPreviewCategory.Walls),
            CreateCategory(all, FinishPreviewCategory.Floors),
            CreateCategory(all, FinishPreviewCategory.Ceilings));
    }

    private static FinishQuantityCategorySummary CreateCategory(
        IEnumerable<FinishOccurrence> occurrences,
        FinishPreviewCategory category)
    {
        FinishOccurrence[] selected = occurrences
            .Where(occurrence => occurrence.Category == category)
            .ToArray();
        return new FinishQuantityCategorySummary(
            selected.Length,
            selected.Select(occurrence => occurrence.RoomId).Distinct().Count(),
            selected.Select(occurrence => occurrence.ElementId).Distinct().Count(),
            selected.Sum(occurrence => occurrence.AreaSquareMeters));
    }
}

public sealed record FinishFaceMeasure(
    double Area,
    double NormalX,
    double NormalY,
    double NormalZ);
