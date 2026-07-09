namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record BimScheduleDryRunReport(
    int AvailableFieldCount,
    int SourceColumnCount,
    int MatchedColumnCount,
    int DataRowCount,
    string? SuggestedKeyColumnName,
    string? SuggestedKeyRevitParameterName,
    int RowsWithKeyCount,
    int RowsMissingKeyCount,
    IReadOnlyList<string> DuplicateKeyValues,
    IReadOnlyList<string> UnmappedColumns,
    IReadOnlyList<string> RequiredUnmappedColumns,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public string ToDialogText()
    {
        List<string> lines =
        [
            "BIM Schedule Mode: dry-run сопоставления",
            $"Полей активной ViewSchedule: {AvailableFieldCount}",
            $"Сопоставлено колонок: {MatchedColumnCount} из {SourceColumnCount}",
            $"Строк данных для будущего сопоставления: {DataRowCount}"
        ];

        if (!string.IsNullOrWhiteSpace(SuggestedKeyColumnName))
        {
            lines.Add($"Ключ для поиска элементов: {SuggestedKeyColumnName} -> {SuggestedKeyRevitParameterName}");
            lines.Add($"Строк с ключом: {RowsWithKeyCount}; без ключа: {RowsMissingKeyCount}");
        }

        if (DuplicateKeyValues.Count > 0)
        {
            lines.Add("Повторяющиеся ключи:");
            lines.AddRange(DuplicateKeyValues.Select(value => $"- {value}"));
        }

        if (UnmappedColumns.Count > 0)
        {
            lines.Add("Колонки без параметра Revit:");
            lines.AddRange(UnmappedColumns.Select(column => $"- {column}"));
        }

        if (RequiredUnmappedColumns.Count > 0)
        {
            lines.Add("Ключевые колонки без сопоставления:");
            lines.AddRange(RequiredUnmappedColumns.Select(column => $"- {column}"));
        }

        if (Warnings.Count > 0)
        {
            lines.Add("Предупреждения:");
            lines.AddRange(Warnings);
        }

        if (Errors.Count > 0)
        {
            lines.Add("Ошибки:");
            lines.AddRange(Errors);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
