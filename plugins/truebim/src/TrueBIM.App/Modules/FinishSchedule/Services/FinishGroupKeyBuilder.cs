using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishGroupKeyBuilder
{
    public const string NoFinishToken = "<NONE>";
    public const string UnknownToken = "<UNKNOWN>";

    public FinishGroupKey Create(RoomFinishSnapshot room)
    {
        if (room is null)
        {
            throw new ArgumentNullException(nameof(room));
        }

        List<string> parts = [];
        AddCategory(parts, "W", room.Walls);
        AddCategory(parts, "F", room.Floors);
        AddCategory(parts, "C", room.Ceilings);
        return new FinishGroupKey(string.Join("|", parts));
    }

    private static void AddCategory(
        List<string> parts,
        string categoryCode,
        RoomFinishCategorySnapshot category)
    {
        if (!category.IsEnabled)
        {
            return;
        }

        string stateToken = category.State switch
        {
            FinishValueState.NoFinish => NoFinishToken,
            FinishValueState.Unknown => UnknownToken,
            _ => "<RESOLVED>"
        };
        string descriptions = string.Concat(category.Items
            .Select(item => item.Description.ComparisonKey)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, NaturalStringComparer.Instance)
            .Select(value => $"{value.Length}:{value}"));
        parts.Add($"{categoryCode}:{stateToken}:{descriptions}");
    }
}
