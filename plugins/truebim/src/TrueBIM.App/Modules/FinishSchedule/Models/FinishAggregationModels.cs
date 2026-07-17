namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum FinishValueState
{
    Resolved,
    NoFinish,
    Unknown
}

public enum FinishAggregationWarningCode
{
    MissingRoomIdentifier,
    MissingDescription,
    UnknownQuantity
}

public sealed record FinishAggregationWarning(
    FinishAggregationWarningCode Code,
    string Message,
    long RoomId,
    long? ElementId = null,
    FinishPreviewCategory? Category = null);

public sealed record NormalizedFinishDescription
{
    public NormalizedFinishDescription(string displayValue, string comparisonKey)
    {
        if (string.IsNullOrWhiteSpace(displayValue))
        {
            throw new ArgumentException("Display value must not be empty.", nameof(displayValue));
        }

        if (string.IsNullOrWhiteSpace(comparisonKey))
        {
            throw new ArgumentException("Comparison key must not be empty.", nameof(comparisonKey));
        }

        DisplayValue = displayValue;
        ComparisonKey = comparisonKey;
    }

    public string DisplayValue { get; }

    public string ComparisonKey { get; }
}

public sealed record RoomFinishItem
{
    public RoomFinishItem(
        NormalizedFinishDescription description,
        double areaSquareMeters)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        if (areaSquareMeters <= 0
            || double.IsNaN(areaSquareMeters)
            || double.IsInfinity(areaSquareMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(areaSquareMeters));
        }

        AreaSquareMeters = areaSquareMeters;
    }

    public NormalizedFinishDescription Description { get; }

    public double AreaSquareMeters { get; }
}

public sealed class RoomFinishCategorySnapshot
{
    public RoomFinishCategorySnapshot(
        bool isEnabled,
        FinishValueState state,
        IEnumerable<RoomFinishItem> items)
    {
        IsEnabled = isEnabled;
        State = state;
        Items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
        if (!isEnabled && Items.Count > 0)
        {
            throw new ArgumentException("Disabled category must not contain finish items.", nameof(items));
        }

        if (state == FinishValueState.NoFinish && Items.Count > 0)
        {
            throw new ArgumentException("No-finish category must not contain finish items.", nameof(items));
        }
    }

    public bool IsEnabled { get; }

    public FinishValueState State { get; }

    public IReadOnlyList<RoomFinishItem> Items { get; }
}

public sealed record RoomFinishSnapshot(
    long RoomId,
    string Identifier,
    RoomFinishCategorySnapshot Walls,
    RoomFinishCategorySnapshot Floors,
    RoomFinishCategorySnapshot Ceilings);

public sealed class RoomFinishSnapshotRequest
{
    public RoomFinishSnapshotRequest(
        FinishScheduleSettings settings,
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        IEnumerable<FinishClassifiedElement> elements,
        IReadOnlyDictionary<long, FinishTypeSnapshot> types,
        FinishQuantityResult quantities)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Rooms = (rooms ?? throw new ArgumentNullException(nameof(rooms)))
            .GroupBy(room => room.ElementId)
            .Select(group => group.First())
            .OrderBy(room => room.ElementId)
            .ToArray();
        Elements = (elements ?? throw new ArgumentNullException(nameof(elements)))
            .GroupBy(element => element.Element.ElementId)
            .ToDictionary(group => group.Key, group => group.First());
        Types = types ?? throw new ArgumentNullException(nameof(types));
        Quantities = quantities ?? throw new ArgumentNullException(nameof(quantities));
    }

    public FinishScheduleSettings Settings { get; }

    public IReadOnlyList<FinishRoomCandidateSnapshot> Rooms { get; }

    public IReadOnlyDictionary<long, FinishClassifiedElement> Elements { get; }

    public IReadOnlyDictionary<long, FinishTypeSnapshot> Types { get; }

    public FinishQuantityResult Quantities { get; }
}

public sealed class RoomFinishSnapshotBuildResult
{
    public RoomFinishSnapshotBuildResult(
        IEnumerable<RoomFinishSnapshot> rooms,
        IEnumerable<FinishAggregationWarning> warnings)
    {
        Rooms = (rooms ?? throw new ArgumentNullException(nameof(rooms)))
            .OrderBy(room => room.RoomId)
            .ToArray();
        Warnings = (warnings ?? throw new ArgumentNullException(nameof(warnings)))
            .OrderBy(warning => warning.RoomId)
            .ThenBy(warning => warning.Category)
            .ThenBy(warning => warning.ElementId)
            .ToArray();
    }

    public IReadOnlyList<RoomFinishSnapshot> Rooms { get; }

    public IReadOnlyList<FinishAggregationWarning> Warnings { get; }
}

public sealed record FinishGroupKey
{
    public FinishGroupKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Group key must not be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public sealed record FinishAggregatedItem(
    NormalizedFinishDescription Description,
    double AreaSquareMeters);

public sealed class FinishAggregatedCategory
{
    public FinishAggregatedCategory(
        bool isEnabled,
        FinishValueState state,
        IEnumerable<FinishAggregatedItem> items)
    {
        IsEnabled = isEnabled;
        State = state;
        Items = (items ?? throw new ArgumentNullException(nameof(items))).ToArray();
    }

    public bool IsEnabled { get; }

    public FinishValueState State { get; }

    public IReadOnlyList<FinishAggregatedItem> Items { get; }
}

public sealed record FinishFormattedCategoryOutput
{
    public FinishFormattedCategoryOutput(string descriptionText, string areaText)
    {
        DescriptionText = descriptionText ?? throw new ArgumentNullException(nameof(descriptionText));
        AreaText = areaText ?? throw new ArgumentNullException(nameof(areaText));
        int descriptionLines = DescriptionText.Split([Environment.NewLine], StringSplitOptions.None).Length;
        int areaLines = AreaText.Split([Environment.NewLine], StringSplitOptions.None).Length;
        if (descriptionLines != areaLines)
        {
            throw new ArgumentException("Description and area output must contain the same number of lines.");
        }
    }

    public string DescriptionText { get; }

    public string AreaText { get; }
}

public sealed record FinishRoomGroupOutput(
    string RoomList,
    FinishFormattedCategoryOutput? Walls,
    FinishFormattedCategoryOutput? Floors,
    FinishFormattedCategoryOutput? Ceilings);

public sealed class FinishAggregatedGroup
{
    public FinishAggregatedGroup(
        FinishGroupKey key,
        IEnumerable<long> roomIds,
        IEnumerable<string> roomIdentifiers,
        FinishAggregatedCategory walls,
        FinishAggregatedCategory floors,
        FinishAggregatedCategory ceilings,
        FinishRoomGroupOutput output)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        RoomIds = (roomIds ?? throw new ArgumentNullException(nameof(roomIds))).ToArray();
        RoomIdentifiers = (roomIdentifiers ?? throw new ArgumentNullException(nameof(roomIdentifiers))).ToArray();
        Walls = walls ?? throw new ArgumentNullException(nameof(walls));
        Floors = floors ?? throw new ArgumentNullException(nameof(floors));
        Ceilings = ceilings ?? throw new ArgumentNullException(nameof(ceilings));
        Output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public FinishGroupKey Key { get; }

    public IReadOnlyList<long> RoomIds { get; }

    public IReadOnlyList<string> RoomIdentifiers { get; }

    public FinishAggregatedCategory Walls { get; }

    public FinishAggregatedCategory Floors { get; }

    public FinishAggregatedCategory Ceilings { get; }

    public FinishRoomGroupOutput Output { get; }
}

public sealed class FinishAggregationResult
{
    public FinishAggregationResult(
        IEnumerable<FinishAggregatedGroup> groups,
        IEnumerable<FinishAggregationWarning> warnings)
    {
        Groups = (groups ?? throw new ArgumentNullException(nameof(groups))).ToArray();
        Warnings = (warnings ?? throw new ArgumentNullException(nameof(warnings))).ToArray();
        RoomOutputs = Groups
            .SelectMany(group => group.RoomIds.Select(roomId => new { roomId, group.Output }))
            .ToDictionary(item => item.roomId, item => item.Output);
        Summary = new FinishAggregationPreviewSummary(
            Groups.Count,
            RoomOutputs.Count,
            Warnings.Count);
    }

    public IReadOnlyList<FinishAggregatedGroup> Groups { get; }

    public IReadOnlyList<FinishAggregationWarning> Warnings { get; }

    public IReadOnlyDictionary<long, FinishRoomGroupOutput> RoomOutputs { get; }

    public FinishAggregationPreviewSummary Summary { get; }
}

public sealed record FinishAggregationPreviewSummary(
    int GroupCount,
    int RoomCount,
    int WarningCount);
