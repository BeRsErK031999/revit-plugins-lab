using System.Text;

namespace TrueBIM.App.Modules.BimTools.CopyParameters.Models;

public sealed record ParameterCopyResult(
    string SourceElementLabel,
    int SelectedParameterCount,
    int TargetElementCount,
    IReadOnlyList<ElementCopyReportRow> Rows)
{
    public int CopiedValueCount => Rows.Count(row => row.Succeeded);

    public int SkippedValueCount => Rows.Count(row => !row.Succeeded);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine("Копирование параметров завершено.");
        builder.AppendLine();
        builder.AppendLine($"Исходный элемент: {SourceElementLabel}");
        builder.AppendLine($"Выбрано параметров: {SelectedParameterCount}");
        builder.AppendLine($"Выбрано элементов-получателей: {TargetElementCount}");
        builder.AppendLine();
        builder.AppendLine($"Успешно скопировано: {CopiedValueCount}");
        builder.AppendLine($"Пропущено: {SkippedValueCount}");

        IReadOnlyList<IGrouping<string, ElementCopyReportRow>> skipGroups = Rows
            .Where(row => !row.Succeeded)
            .GroupBy(row => row.Message)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (skipGroups.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Причины пропуска:");
            foreach (IGrouping<string, ElementCopyReportRow> group in skipGroups.Take(6))
            {
                builder.AppendLine($"- {group.Key}: {group.Count()}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
