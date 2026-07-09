using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ParsedTableValidationService
{
    public ParsedTableValidationResult Validate(ParsedTable table)
    {
        Guard.NotNull(table, nameof(table));

        List<string> warnings = [];
        List<string> errors = [];
        if (table.IsEmpty)
        {
            errors.Add("Таблица не содержит строк, колонок или ячеек.");
            return new ParsedTableValidationResult(warnings, errors);
        }

        if (table.RowCount > 80)
        {
            warnings.Add("В таблице больше 80 строк: визуальная таблица может получиться слишком крупной для текущего вида.");
        }

        if (table.ColumnCount > 12)
        {
            warnings.Add("В таблице больше 12 колонок: проверьте ширину после создания на виде.");
        }

        foreach (ParsedCell cell in table.Cells)
        {
            if (cell.RowIndex < 0 || cell.ColumnIndex < 0)
            {
                errors.Add("Найдена ячейка с отрицательным индексом строки или колонки.");
                continue;
            }

            if (cell.RowIndex >= table.RowCount || cell.ColumnIndex >= table.ColumnCount)
            {
                errors.Add($"Ячейка [{cell.RowIndex + 1}; {cell.ColumnIndex + 1}] выходит за границы таблицы.");
                continue;
            }

            if (cell.RowSpan > 1 || cell.ColumnSpan > 1)
            {
                warnings.Add($"Ячейка [{cell.RowIndex + 1}; {cell.ColumnIndex + 1}] имеет объединение. MVP нарисует текст в верхней левой позиции объединения.");
            }
        }

        if (table.Confidence > 0 && table.Confidence < 0.6)
        {
            warnings.Add("Низкая уверенность распознавания: проверьте preview перед созданием визуальной таблицы.");
        }

        return new ParsedTableValidationResult(warnings.Distinct().ToList(), errors.Distinct().ToList());
    }
}

public sealed record ParsedTableValidationResult(
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}
