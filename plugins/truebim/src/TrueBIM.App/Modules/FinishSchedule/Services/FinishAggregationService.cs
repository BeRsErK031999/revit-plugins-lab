using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishAggregationService
{
    private readonly FinishGroupKeyBuilder keyBuilder;
    private readonly FinishAggregationFormatter formatter;

    public FinishAggregationService(
        FinishGroupKeyBuilder keyBuilder,
        FinishAggregationFormatter formatter)
    {
        this.keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
        this.formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public FinishAggregationResult Aggregate(RoomFinishSnapshotBuildResult snapshots)
    {
        if (snapshots is null)
        {
            throw new ArgumentNullException(nameof(snapshots));
        }

        FinishAggregatedGroup[] groups = snapshots.Rooms
            .GroupBy(room => room.Identifier, StringComparer.OrdinalIgnoreCase)
            .Select(CreateLogicalRoom)
            .GroupBy(item => item.Key.Value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(CreateGroup)
            .ToArray();
        return new FinishAggregationResult(groups, snapshots.Warnings);
    }

    private KeyedRoom CreateLogicalRoom(IGrouping<string, RoomFinishSnapshot> group)
    {
        RoomFinishSnapshot[] rooms = group.OrderBy(room => room.RoomId).ToArray();
        // Repeated identifiers represent one room in the report, including all of its finishes.
        RoomFinishSnapshot combined = new(rooms[0].RoomId, rooms[0].Identifier,
            Combine(rooms.Select(room => room.Walls)),
            Combine(rooms.Select(room => room.Floors)),
            Combine(rooms.Select(room => room.Ceilings)));
        return new KeyedRoom(keyBuilder.Create(combined), combined, rooms.Select(room => room.RoomId).ToArray());
    }

    private static RoomFinishCategorySnapshot Combine(IEnumerable<RoomFinishCategorySnapshot> categories)
    {
        FinishAggregatedCategory combined = AggregateCategory(categories);
        return new RoomFinishCategorySnapshot(combined.IsEnabled, combined.State,
            combined.Items.Select(item => new RoomFinishItem(item.Description, item.AreaSquareMeters)));
    }

    private FinishAggregatedGroup CreateGroup(IGrouping<string, KeyedRoom> group)
    {
        KeyedRoom[] orderedRooms = group
            .OrderBy(item => item.Room.Identifier, NaturalStringComparer.Instance)
            .ThenBy(item => item.Room.RoomId)
            .ToArray();
        FinishAggregatedCategory walls = AggregateCategory(
            orderedRooms.Select(item => item.Room.Walls));
        FinishAggregatedCategory floors = AggregateCategory(
            orderedRooms.Select(item => item.Room.Floors));
        FinishAggregatedCategory ceilings = AggregateCategory(
            orderedRooms.Select(item => item.Room.Ceilings));
        string[] identifiers = orderedRooms.Select(item => item.Room.Identifier).ToArray();
        FinishRoomGroupOutput output = formatter.Format(identifiers, walls, floors, ceilings);
        return new FinishAggregatedGroup(
            orderedRooms[0].Key,
            orderedRooms.SelectMany(item => item.PhysicalRoomIds),
            identifiers,
            walls,
            floors,
            ceilings,
            output);
    }

    private static FinishAggregatedCategory AggregateCategory(
        IEnumerable<RoomFinishCategorySnapshot> source)
    {
        RoomFinishCategorySnapshot[] categories = source.ToArray();
        bool isEnabled = categories.Any(category => category.IsEnabled);
        if (!isEnabled)
        {
            return new FinishAggregatedCategory(false, FinishValueState.NoFinish, []);
        }

        FinishValueState state = categories.Any(category => category.State == FinishValueState.Unknown)
            ? FinishValueState.Unknown
            : categories.All(category => category.State == FinishValueState.NoFinish)
                ? FinishValueState.NoFinish
                : FinishValueState.Resolved;
        FinishAggregatedItem[] items = categories
            .SelectMany(category => category.Items)
            .GroupBy(item => item.Description.ComparisonKey, StringComparer.Ordinal)
            .Select(group => new FinishAggregatedItem(
                new NormalizedFinishDescription(
                    group.Select(item => item.Description.DisplayValue)
                        .OrderBy(value => value, NaturalStringComparer.Instance)
                        .First(),
                    group.Key),
                group.Sum(item => item.AreaSquareMeters)))
            .OrderBy(item => item.Description.DisplayValue, NaturalStringComparer.Instance)
            .ToArray();
        return new FinishAggregatedCategory(true, state, items);
    }

    private sealed record KeyedRoom(FinishGroupKey Key, RoomFinishSnapshot Room, IReadOnlyList<long> PhysicalRoomIds);
}
