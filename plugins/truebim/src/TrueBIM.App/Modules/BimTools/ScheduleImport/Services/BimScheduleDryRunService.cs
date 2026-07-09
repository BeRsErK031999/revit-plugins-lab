using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class BimScheduleDryRunService
{
    public BimScheduleDryRunReport CreateReport(
        ParsedTable table,
        IReadOnlyList<ColumnMapping> mappings,
        ScheduleImportContext context)
    {
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(mappings, nameof(mappings));
        Guard.NotNull(context, nameof(context));

        List<string> warnings =
        [
            "Dry-run не изменяет модель Revit. Запись значений в параметры будет отдельным этапом после выбора ключевого поля и подтверждения."
        ];
        List<string> errors = [];
        if (!context.CanUseBimScheduleMode)
        {
            errors.Add("Для BIM Schedule Mode откройте обычную ViewSchedule и повторите проверку.");
        }

        if (context.CanUseBimScheduleMode && context.AvailableBimScheduleParameterNames.Count == 0)
        {
            errors.Add("В активной ViewSchedule не найдены доступные поля для сопоставления.");
        }

        IReadOnlyList<string> unmappedColumns = mappings
            .Where(mapping => string.IsNullOrWhiteSpace(mapping.TargetRevitParameterName))
            .Select(mapping => mapping.SourceColumnName)
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        IReadOnlyList<string> requiredUnmappedColumns = mappings
            .Where(mapping => mapping.IsRequired && string.IsNullOrWhiteSpace(mapping.TargetRevitParameterName))
            .Select(mapping => mapping.SourceColumnName)
            .Where(column => !string.IsNullOrWhiteSpace(column))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        int matchedColumnCount = mappings.Count(mapping => !string.IsNullOrWhiteSpace(mapping.TargetRevitParameterName));
        ColumnMapping? keyMapping = ResolveKeyMapping(mappings);
        KeyValueStats keyStats = keyMapping is null
            ? KeyValueStats.Empty
            : AnalyzeKeyValues(table, keyMapping.SourceColumnName);

        if (unmappedColumns.Count > 0)
        {
            warnings.Add($"Не сопоставлены колонки: {string.Join(", ", unmappedColumns)}.");
        }

        if (requiredUnmappedColumns.Count > 0)
        {
            warnings.Add($"Ключевые колонки без сопоставления: {string.Join(", ", requiredUnmappedColumns)}.");
        }

        if (keyMapping is null)
        {
            warnings.Add("Не выбран ключ для поиска элементов модели. Для будущей записи нужен столбец вроде Марка, Номер, Позиция или ID, сопоставленный с параметром Revit.");
        }
        else
        {
            if (keyStats.RowsMissingKeyCount > 0)
            {
                warnings.Add($"Строки без значения ключа '{keyMapping.SourceColumnName}': {keyStats.RowsMissingKeyCount}.");
            }

            if (keyStats.DuplicateKeyValues.Count > 0)
            {
                warnings.Add($"Повторяющиеся значения ключа '{keyMapping.SourceColumnName}': {string.Join(", ", keyStats.DuplicateKeyValues)}.");
            }
        }

        return new BimScheduleDryRunReport(
            context.AvailableBimScheduleParameterNames.Count,
            table.ColumnCount,
            matchedColumnCount,
            CountDataRows(table),
            keyMapping?.SourceColumnName,
            keyMapping?.TargetRevitParameterName,
            keyStats.RowsWithKeyCount,
            keyStats.RowsMissingKeyCount,
            keyStats.DuplicateKeyValues,
            unmappedColumns,
            requiredUnmappedColumns,
            warnings.Distinct(StringComparer.CurrentCulture).ToList(),
            errors.Distinct(StringComparer.CurrentCulture).ToList());
    }

    private static ColumnMapping? ResolveKeyMapping(IReadOnlyList<ColumnMapping> mappings)
    {
        return mappings.FirstOrDefault(mapping =>
                mapping.IsRequired && !string.IsNullOrWhiteSpace(mapping.TargetRevitParameterName))
            ?? mappings.FirstOrDefault(mapping => !string.IsNullOrWhiteSpace(mapping.TargetRevitParameterName));
    }

    private static KeyValueStats AnalyzeKeyValues(ParsedTable table, string keyColumnName)
    {
        int keyColumnIndex = ResolveColumnIndex(table, keyColumnName);
        if (keyColumnIndex < 0)
        {
            return KeyValueStats.Empty;
        }

        Dictionary<string, int> values = new(StringComparer.CurrentCultureIgnoreCase);
        int rowsWithKey = 0;
        int rowsMissingKey = 0;
        foreach (ParsedRow row in EnumerateDataRows(table))
        {
            string value = keyColumnIndex < row.Values.Count
                ? row.Values[keyColumnIndex]?.Trim() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                rowsMissingKey++;
                continue;
            }

            rowsWithKey++;
            values[value] = values.TryGetValue(value, out int count) ? count + 1 : 1;
        }

        IReadOnlyList<string> duplicates = values
            .Where(pair => pair.Value > 1)
            .Select(pair => pair.Key)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new KeyValueStats(rowsWithKey, rowsMissingKey, duplicates);
    }

    private static int ResolveColumnIndex(ParsedTable table, string columnName)
    {
        for (int index = 0; index < table.Columns.Count; index++)
        {
            if (string.Equals(table.Columns[index]?.Trim(), columnName.Trim(), StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static int CountDataRows(ParsedTable table)
    {
        return EnumerateDataRows(table).Count();
    }

    private static IEnumerable<ParsedRow> EnumerateDataRows(ParsedTable table)
    {
        int headerRows = HasHeaderRow(table) ? 1 : 0;
        return table.Rows
            .OrderBy(row => row.RowIndex)
            .Skip(headerRows);
    }

    private static bool HasHeaderRow(ParsedTable table)
    {
        if (table.Cells.Any(cell => cell.RowIndex == 0 && cell.IsHeader))
        {
            return true;
        }

        if (table.Rows.Count == 0 || table.Columns.Count == 0)
        {
            return false;
        }

        IReadOnlyList<string> firstRow = table.Rows[0].Values;
        return firstRow.Count >= table.Columns.Count
            && table.Columns
                .Select((column, index) => index < firstRow.Count
                    && string.Equals(column?.Trim(), firstRow[index]?.Trim(), StringComparison.CurrentCultureIgnoreCase))
                .All(matches => matches);
    }

    private sealed record KeyValueStats(
        int RowsWithKeyCount,
        int RowsMissingKeyCount,
        IReadOnlyList<string> DuplicateKeyValues)
    {
        public static KeyValueStats Empty { get; } = new(
            0,
            0,
            Array.Empty<string>());
    }
}
