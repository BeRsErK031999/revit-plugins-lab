namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum FinishPhysicalCategory
{
    Wall,
    Floor
}

public enum FinishPreviewCategory
{
    Walls,
    Floors,
    Ceilings
}

public sealed record AxisAlignedBox3D
{
    public AxisAlignedBox3D(
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        if (!IsFinite(minX)
            || !IsFinite(minY)
            || !IsFinite(minZ)
            || !IsFinite(maxX)
            || !IsFinite(maxY)
            || !IsFinite(maxZ))
        {
            throw new ArgumentOutOfRangeException(nameof(minX), "Bounds must contain finite coordinates.");
        }

        if (minX > maxX || minY > maxY || minZ > maxZ)
        {
            throw new ArgumentException("Bounds minimum coordinates must not exceed maximum coordinates.");
        }

        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public double MinX { get; }

    public double MinY { get; }

    public double MinZ { get; }

    public double MaxX { get; }

    public double MaxY { get; }

    public double MaxZ { get; }

    public bool Intersects(AxisAlignedBox3D other, double tolerance = 0)
    {
        if (other is null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        if (tolerance < 0 || double.IsNaN(tolerance) || double.IsInfinity(tolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        return MinX <= other.MaxX + tolerance
            && MaxX + tolerance >= other.MinX
            && MinY <= other.MaxY + tolerance
            && MaxY + tolerance >= other.MinY
            && MinZ <= other.MaxZ + tolerance
            && MaxZ + tolerance >= other.MinZ;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public sealed record FinishParameterValueSnapshot(string RawValue, string DisplayValue)
{
    public bool Matches(string expected)
    {
        string normalized = expected?.Trim() ?? string.Empty;
        return string.Equals(RawValue.Trim(), normalized, StringComparison.OrdinalIgnoreCase)
            || string.Equals(DisplayValue.Trim(), normalized, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FinishRoomCandidateSnapshot
{
    private readonly IReadOnlyDictionary<string, FinishParameterValueSnapshot> parameterValues;

    public FinishRoomCandidateSnapshot(
        long elementId,
        long levelId,
        double area,
        bool hasLocation,
        AxisAlignedBox3D? bounds,
        IReadOnlyDictionary<string, FinishParameterValueSnapshot>? parameterValues = null,
        string number = "",
        string name = "")
    {
        ElementId = elementId;
        LevelId = levelId;
        Area = area;
        HasLocation = hasLocation;
        Bounds = bounds;
        Number = number ?? string.Empty;
        Name = name ?? string.Empty;
        this.parameterValues = parameterValues is null
            ? new Dictionary<string, FinishParameterValueSnapshot>(StringComparer.Ordinal)
            : parameterValues.ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.Ordinal);
    }

    public long ElementId { get; }

    public long LevelId { get; }

    public double Area { get; }

    public bool HasLocation { get; }

    public AxisAlignedBox3D? Bounds { get; }

    public string Number { get; }

    public string Name { get; }

    public bool TryGetParameterValue(
        ParameterReference reference,
        out FinishParameterValueSnapshot? value)
    {
        if (reference is null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        return parameterValues.TryGetValue(reference.StableKey, out value);
    }
}

public sealed record FinishElementCandidateSnapshot(
    long ElementId,
    long TypeId,
    FinishPhysicalCategory PhysicalCategory,
    AxisAlignedBox3D? Bounds);

public sealed record FinishTypeSnapshot(
    long TypeId,
    string? ClassificationValue,
    bool HasClassificationParameter,
    FinishParameterValueSnapshot? DescriptionValue = null,
    bool HasDescriptionParameter = false);

public sealed class FinishElementCollection
{
    public FinishElementCollection(
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        IEnumerable<FinishElementCandidateSnapshot> walls,
        IEnumerable<FinishElementCandidateSnapshot> floors,
        IEnumerable<FinishTypeSnapshot> types)
    {
        Rooms = (rooms ?? throw new ArgumentNullException(nameof(rooms)))
            .OrderBy(room => room.ElementId)
            .ToArray();
        Walls = (walls ?? throw new ArgumentNullException(nameof(walls)))
            .OrderBy(element => element.ElementId)
            .ToArray();
        Floors = (floors ?? throw new ArgumentNullException(nameof(floors)))
            .OrderBy(element => element.ElementId)
            .ToArray();
        Types = (types ?? throw new ArgumentNullException(nameof(types)))
            .GroupBy(type => type.TypeId)
            .ToDictionary(group => group.Key, group => group.Last());
    }

    public IReadOnlyList<FinishRoomCandidateSnapshot> Rooms { get; }

    public IReadOnlyList<FinishElementCandidateSnapshot> Walls { get; }

    public IReadOnlyList<FinishElementCandidateSnapshot> Floors { get; }

    public IReadOnlyDictionary<long, FinishTypeSnapshot> Types { get; }
}

public enum FinishRoomSkipReason
{
    Unplaced,
    NotEnclosed,
    MissingBounds
}

public sealed record FinishRoomScopeSkip(long ElementId, FinishRoomSkipReason Reason);

public sealed class FinishRoomScopeResult
{
    public FinishRoomScopeResult(
        IEnumerable<FinishRoomCandidateSnapshot> selectedRooms,
        IEnumerable<FinishRoomScopeSkip> invalidRooms,
        int outsideScopeCount,
        int missingScopeValueCount)
    {
        SelectedRooms = (selectedRooms ?? throw new ArgumentNullException(nameof(selectedRooms)))
            .OrderBy(room => room.ElementId)
            .ToArray();
        InvalidRooms = (invalidRooms ?? throw new ArgumentNullException(nameof(invalidRooms)))
            .OrderBy(room => room.ElementId)
            .ToArray();
        OutsideScopeCount = outsideScopeCount;
        MissingScopeValueCount = missingScopeValueCount;
    }

    public IReadOnlyList<FinishRoomCandidateSnapshot> SelectedRooms { get; }

    public IReadOnlyList<FinishRoomScopeSkip> InvalidRooms { get; }

    public int OutsideScopeCount { get; }

    public int MissingScopeValueCount { get; }
}

public enum FinishClassificationSkipReason
{
    MissingType,
    MissingClassificationParameter,
    EmptyClassificationValue,
    ValueDoesNotMatch,
    AmbiguousFloorClassification
}

public sealed record FinishClassificationSkip(
    long ElementId,
    FinishClassificationSkipReason Reason);

public sealed record FinishClassifiedElement(
    FinishElementCandidateSnapshot Element,
    FinishPreviewCategory Category);

public sealed class FinishClassificationResult
{
    public FinishClassificationResult(
        IEnumerable<FinishClassifiedElement> elements,
        IEnumerable<FinishClassificationSkip> skippedElements)
    {
        Elements = (elements ?? throw new ArgumentNullException(nameof(elements)))
            .OrderBy(element => element.Element.ElementId)
            .ToArray();
        SkippedElements = (skippedElements ?? throw new ArgumentNullException(nameof(skippedElements)))
            .OrderBy(element => element.ElementId)
            .ToArray();
    }

    public IReadOnlyList<FinishClassifiedElement> Elements { get; }

    public IReadOnlyList<FinishClassificationSkip> SkippedElements { get; }
}

public sealed record FinishPreviewCategoryCounts(
    int SourceCollected,
    int Classified,
    int InScope);

public sealed record FinishPreviewIndexCounts(
    int IndexedElements,
    int ElementsWithoutBounds,
    int PotentialRoomElementPairs);

public sealed class FinishSchedulePreviewResult
{
    public FinishSchedulePreviewResult(
        int collectedRooms,
        FinishRoomScopeResult roomScope,
        FinishPreviewCategoryCounts walls,
        FinishPreviewCategoryCounts floors,
        FinishPreviewCategoryCounts ceilings,
        FinishPreviewIndexCounts index,
        IEnumerable<string> warnings,
        FinishQuantityPreviewSummary? quantities = null,
        FinishAggregationPreviewSummary? aggregation = null,
        FinishSchedulePerformanceSummary? performance = null)
    {
        CollectedRooms = collectedRooms;
        RoomScope = roomScope ?? throw new ArgumentNullException(nameof(roomScope));
        Walls = walls;
        Floors = floors;
        Ceilings = ceilings;
        Index = index;
        Warnings = (warnings ?? throw new ArgumentNullException(nameof(warnings))).ToArray();
        Quantities = quantities;
        Aggregation = aggregation;
        Performance = performance;
    }

    public int CollectedRooms { get; }

    public FinishRoomScopeResult RoomScope { get; }

    public FinishPreviewCategoryCounts Walls { get; }

    public FinishPreviewCategoryCounts Floors { get; }

    public FinishPreviewCategoryCounts Ceilings { get; }

    public FinishPreviewIndexCounts Index { get; }

    public IReadOnlyList<string> Warnings { get; }

    public FinishQuantityPreviewSummary? Quantities { get; }

    public FinishAggregationPreviewSummary? Aggregation { get; }

    public FinishSchedulePerformanceSummary? Performance { get; }

    public FinishSchedulePreviewResult WithQuantities(FinishQuantityResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        string[] mergedWarnings = Warnings
            .Concat(result.Warnings.Select(warning => warning.Message))
            .GroupBy(warning => warning, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToArray();
        return new FinishSchedulePreviewResult(
            CollectedRooms,
            RoomScope,
            Walls,
            Floors,
            Ceilings,
            Index,
            mergedWarnings,
            result.Summary,
            Aggregation,
            Performance);
    }

    public FinishSchedulePreviewResult WithAggregation(FinishAggregationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        string[] mergedWarnings = Warnings
            .Concat(result.Warnings.Select(warning => warning.Message))
            .GroupBy(warning => warning, StringComparer.Ordinal)
            .Select(group => group.Key)
            .ToArray();
        return new FinishSchedulePreviewResult(
            CollectedRooms,
            RoomScope,
            Walls,
            Floors,
            Ceilings,
            Index,
            mergedWarnings,
            Quantities,
            result.Summary,
            Performance);
    }

    public FinishSchedulePreviewResult WithPerformance(FinishSchedulePerformanceSummary performance)
    {
        return new FinishSchedulePreviewResult(
            CollectedRooms,
            RoomScope,
            Walls,
            Floors,
            Ceilings,
            Index,
            Warnings,
            Quantities,
            Aggregation,
            performance ?? throw new ArgumentNullException(nameof(performance)));
    }
}

public sealed record FinishSchedulePreviewBuild(
    FinishSchedulePreviewResult Preview,
    FinishRoomScopeResult RoomScope,
    FinishClassificationResult Classification,
    IReadOnlyList<FinishClassifiedElement> InScopeElements);
