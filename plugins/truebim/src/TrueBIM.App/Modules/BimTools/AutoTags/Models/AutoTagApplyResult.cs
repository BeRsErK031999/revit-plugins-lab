using System.Text;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Models;

public sealed record AutoTagApplyResult(IReadOnlyList<AutoTagReportRow> Rows)
{
    public int CreatedCount => Rows.Count(row => row.Status == AutoTagStatuses.Done);

    public int SkippedCount => Rows.Count(row => row.Status == AutoTagStatuses.Skipped);

    public int FailedCount => Rows.Count(row => row.Status == AutoTagStatuses.Error);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Создано марок: {CreatedCount}");
        builder.AppendLine($"Пропущено: {SkippedCount}");
        builder.AppendLine($"Ошибок: {FailedCount}");

        IReadOnlyList<AutoTagReportRow> importantRows = Rows
            .Where(row => row.Status != AutoTagStatuses.Done)
            .Take(8)
            .ToList();
        if (importantRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Сообщения:");
            foreach (AutoTagReportRow row in importantRows)
            {
                builder.AppendLine($"- {row.ElementId}: {row.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
