using System.Text.RegularExpressions;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ParameterMappingService
{
    public IReadOnlyDictionary<string, string> SuggestMappings(
        ParsedTable table,
        IReadOnlyCollection<ScheduleFieldOption> availableFields)
    {
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(availableFields, nameof(availableFields));

        Dictionary<string, ScheduleFieldOption> fieldsByName = availableFields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .OrderBy(field => FieldTypePriority(field.FieldTypeName))
            .ThenBy(field => field.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .GroupBy(field => NormalizeKey(field.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return table.Columns
            .Select(column => new
            {
                Column = column,
                Field = ResolveField(column, fieldsByName)
            })
            .Where(item => item.Field is not null)
            .GroupBy(item => item.Column, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().Field!.Key,
                StringComparer.CurrentCultureIgnoreCase);
    }

    private static ScheduleFieldOption? ResolveField(
        string columnName,
        IReadOnlyDictionary<string, ScheduleFieldOption> fieldsByName)
    {
        foreach (string key in BuildColumnMatchKeys(columnName))
        {
            if (fieldsByName.TryGetValue(key, out ScheduleFieldOption? field))
            {
                return field;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> BuildColumnMatchKeys(string columnName)
    {
        List<string> keys = [];
        AddKey(keys, columnName);
        AddKey(keys, Regex.Replace(columnName, @"\([^)]*\)", string.Empty));
        AddKey(keys, Regex.Replace(columnName, @"(?:,|\s)+(мм|mm|м|m|м2|m2|м3|m3)$", string.Empty, RegexOptions.IgnoreCase));
        AddKey(keys, Regex.Replace(columnName, @"(?:,|\s)+(шт|pcs|qty)$", string.Empty, RegexOptions.IgnoreCase));
        return keys;
    }

    private static void AddKey(List<string> keys, string value)
    {
        string key = NormalizeKey(value);
        if (key.Length > 0 && !keys.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            keys.Add(key);
        }
    }

    private static int FieldTypePriority(string fieldTypeName)
    {
        return fieldTypeName switch
        {
            "Instance" => 0,
            "ElementType" => 1,
            _ => 2
        };
    }

    private static string NormalizeKey(string value)
    {
        string lower = value.Trim().ToLowerInvariant();
        return Regex.Replace(lower, @"[\s\p{P}\p{S}]+", string.Empty);
    }
}
