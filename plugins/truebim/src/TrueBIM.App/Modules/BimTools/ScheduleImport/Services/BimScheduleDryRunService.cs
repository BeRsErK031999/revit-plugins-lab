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

        if (unmappedColumns.Count > 0)
        {
            warnings.Add($"Не сопоставлены колонки: {string.Join(", ", unmappedColumns)}.");
        }

        if (requiredUnmappedColumns.Count > 0)
        {
            warnings.Add($"Ключевые колонки без сопоставления: {string.Join(", ", requiredUnmappedColumns)}.");
        }

        return new BimScheduleDryRunReport(
            context.AvailableBimScheduleParameterNames.Count,
            table.ColumnCount,
            matchedColumnCount,
            CountDataRows(table),
            unmappedColumns,
            requiredUnmappedColumns,
            warnings.Distinct(StringComparer.CurrentCulture).ToList(),
            errors.Distinct(StringComparer.CurrentCulture).ToList());
    }

    private static int CountDataRows(ParsedTable table)
    {
        int headerRows = HasHeaderRow(table) ? 1 : 0;
        return Math.Max(0, table.RowCount - headerRows);
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
}
