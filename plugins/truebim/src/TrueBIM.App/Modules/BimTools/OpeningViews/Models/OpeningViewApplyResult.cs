using System.Text;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed record OpeningViewApplyResult(IReadOnlyList<OpeningViewReportRow> Rows)
{
    public int CreatedCount => Rows.Count(row => row.Status == OpeningViewStatuses.Created);

    public int SkippedCount => Rows.Count(row => row.Status == OpeningViewStatuses.Skipped);

    public int FailedCount => Rows.Count(row => row.Status == OpeningViewStatuses.Error);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Создано видов: {CreatedCount}");
        builder.AppendLine($"Пропущено: {SkippedCount}");
        builder.AppendLine($"Ошибок: {FailedCount}");

        IReadOnlyList<OpeningViewReportRow> importantRows = Rows
            .Where(row => row.Status != OpeningViewStatuses.Created)
            .Take(8)
            .ToList();
        if (importantRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Сообщения:");
            foreach (OpeningViewReportRow row in importantRows)
            {
                builder.AppendLine($"- {row.ElementId}: {row.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
