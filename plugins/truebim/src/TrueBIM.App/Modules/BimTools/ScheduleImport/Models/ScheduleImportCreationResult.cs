namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportCreationResult(
    string ScheduleName,
    long? ScheduleId,
    bool OpenedInSeparateTab,
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
            $"Спецификация: {ScheduleName}",
            $"Импортировано строк: {RowCount}",
            $"Импортировано колонок: {ColumnCount}",
            "Создана новая спецификация Revit, доступная в диспетчере проекта и для размещения на листе."
        ];

        if (OpenedInSeparateTab)
        {
            lines.Add("Спецификация открыта в отдельной вкладке Revit.");
        }

        if (Warnings.Count > 0)
        {
            lines.Add("Предупреждения:");
            lines.AddRange(Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
