namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleTableImportResult(
    string TargetScheduleName,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public string ToDialogText()
    {
        if (!Succeeded)
        {
            return string.Join(Environment.NewLine, Errors);
        }

        List<string> lines =
        [
            $"Спецификация: {TargetScheduleName}",
            $"Импортировано строк: {RowCount}",
            $"Импортировано колонок: {ColumnCount}",
            "Исходное тело спецификации скрыто; распознанная таблица записана в редактируемую секцию заголовка."
        ];

        if (Warnings.Count > 0)
        {
            lines.Add("Предупреждения:");
            lines.AddRange(Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
