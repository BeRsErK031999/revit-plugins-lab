namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record DraftingTableCreationResult(
    string TargetViewName,
    bool CreatedNewView,
    int CreatedLineCount,
    int CreatedTextCount,
    IReadOnlyList<string> SkippedCells,
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
            $"Вид: {TargetViewName}",
            $"Создано линий: {CreatedLineCount}",
            $"Создано текстов: {CreatedTextCount}"
        ];

        if (CreatedNewView)
        {
            lines.Add("Создан новый чертёжный вид для визуальной таблицы.");
        }

        if (SkippedCells.Count > 0)
        {
            lines.Add("Пропущено ячеек: " + SkippedCells.Count);
        }

        if (Warnings.Count > 0)
        {
            lines.Add("Предупреждения:");
            lines.AddRange(Warnings);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
