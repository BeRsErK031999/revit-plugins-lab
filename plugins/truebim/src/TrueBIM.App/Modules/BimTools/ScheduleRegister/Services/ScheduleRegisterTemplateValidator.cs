using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;

public sealed class ScheduleRegisterTemplateValidator
{
    private static readonly string[] ExpectedColumnTitles =
    [
        ScheduleRegisterConstants.SheetColumnTitle,
        ScheduleRegisterConstants.NameColumnTitle,
        ScheduleRegisterConstants.NoteColumnTitle
    ];

    public ScheduleRegisterTemplateValidation Validate(ScheduleRegisterTemplateSnapshot snapshot)
    {
        Guard.NotNull(snapshot, nameof(snapshot));
        List<string> issues = [];
        IReadOnlyList<IReadOnlyList<string>> rows = snapshot.HeaderRows;
        if (rows.Count < 3)
        {
            return ScheduleRegisterTemplateValidation.Missing(
                "В шапке должно быть не менее трёх строк: заголовок, названия столбцов и пустая строка данных.");
        }

        int titleRowIndex = 0;
        bool titleMatches = rows[titleRowIndex]
            .Any(value => EqualsNormalized(value, ScheduleRegisterConstants.ScheduleTitle));
        if (!titleMatches)
        {
            issues.Add($"Первая строка должна содержать заголовок «{ScheduleRegisterConstants.ScheduleTitle}».");
        }

        int columnHeaderRowIndex = FindColumnHeaderRow(rows);
        if (columnHeaderRowIndex < 0)
        {
            issues.Add(
                "Не найдена строка с колонками «Лист», «Наименование», «Примечание» в указанном порядке.");
        }

        int dataStartRowIndex = columnHeaderRowIndex + 1;
        int dataRowCount = columnHeaderRowIndex < 0 ? 0 : rows.Count - dataStartRowIndex;
        if (columnHeaderRowIndex >= 0 && dataRowCount == 0)
        {
            issues.Add("После названий столбцов должна оставаться хотя бы одна пустая строка шаблона.");
        }
        else if (columnHeaderRowIndex >= 0)
        {
            for (int rowIndex = dataStartRowIndex; rowIndex < rows.Count; rowIndex++)
            {
                if (rows[rowIndex].Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    issues.Add("Строки данных шаблона должны быть пустыми.");
                    break;
                }
            }
        }

        return new ScheduleRegisterTemplateValidation(
            issues.Count == 0,
            titleRowIndex,
            columnHeaderRowIndex,
            dataStartRowIndex,
            dataRowCount,
            issues);
    }

    private static int FindColumnHeaderRow(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            IReadOnlyList<string> row = rows[rowIndex];
            if (row.Count < ExpectedColumnTitles.Length)
            {
                continue;
            }

            bool matches = true;
            for (int columnIndex = 0; columnIndex < ExpectedColumnTitles.Length; columnIndex++)
            {
                if (!EqualsNormalized(row[columnIndex], ExpectedColumnTitles[columnIndex]))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return rowIndex;
            }
        }

        return -1;
    }

    private static bool EqualsNormalized(string? left, string right)
    {
        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.CurrentCultureIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return string.Join(
            " ",
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
