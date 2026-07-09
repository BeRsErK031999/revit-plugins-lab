using System.Text.RegularExpressions;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ParameterMappingService
{
    public IReadOnlyList<ColumnMapping> SuggestMappings(
        ParsedTable table,
        IReadOnlyCollection<string> availableParameterNames)
    {
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(availableParameterNames, nameof(availableParameterNames));

        Dictionary<string, string> parametersByKey = availableParameterNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(NormalizeKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return table.Columns
            .Select(column =>
            {
                string key = NormalizeKey(column);
                parametersByKey.TryGetValue(key, out string? parameterName);
                return new ColumnMapping(
                    column,
                    parameterName,
                    null,
                    InferDataType(column),
                    InferSourceUnit(column),
                    null,
                    IsLikelyRequired(column));
            })
            .ToList();
    }

    private static ScheduleImportDataType InferDataType(string columnName)
    {
        string normalized = NormalizeKey(columnName);
        if (ContainsAny(normalized, "length", "длина", "len"))
        {
            return ScheduleImportDataType.Length;
        }

        if (ContainsAny(normalized, "area", "площадь"))
        {
            return ScheduleImportDataType.Area;
        }

        if (ContainsAny(normalized, "volume", "объем", "объём"))
        {
            return ScheduleImportDataType.Volume;
        }

        if (ContainsAny(normalized, "count", "количество", "колво", "qty"))
        {
            return ScheduleImportDataType.Count;
        }

        if (ContainsAny(normalized, "diameter", "диаметр", "dn"))
        {
            return ScheduleImportDataType.Length;
        }

        return ScheduleImportDataType.Text;
    }

    private static string? InferSourceUnit(string columnName)
    {
        string normalized = NormalizeKey(columnName);
        if (ContainsAny(normalized, "мм", "mm"))
        {
            return "mm";
        }

        if (ContainsAny(normalized, "м2", "m2"))
        {
            return "m2";
        }

        if (ContainsAny(normalized, "м3", "m3"))
        {
            return "m3";
        }

        if (ContainsAny(normalized, "м", "m"))
        {
            return "m";
        }

        return null;
    }

    private static bool IsLikelyRequired(string columnName)
    {
        string normalized = NormalizeKey(columnName);
        return ContainsAny(normalized, "марка", "номер", "позиция", "поз", "id");
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NormalizeKey(string value)
    {
        string lower = value.Trim().ToLowerInvariant();
        return Regex.Replace(lower, @"[\s\p{P}\p{S}]+", string.Empty);
    }
}
