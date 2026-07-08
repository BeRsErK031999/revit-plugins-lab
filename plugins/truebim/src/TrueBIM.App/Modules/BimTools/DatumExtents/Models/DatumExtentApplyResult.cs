using System.Text;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Models;

public sealed record DatumExtentApplyResult(IReadOnlyList<DatumExtentReportRow> Rows)
{
    public int ChangedCount => Rows.Count(row => row.Status == DatumExtentStatuses.Done);

    public int UnchangedCount => Rows.Count(row => row.Status == DatumExtentStatuses.Unchanged);

    public int SkippedCount => Rows.Count(row => row.Status == DatumExtentStatuses.Skipped);

    public int FailedCount => Rows.Count(row => row.Status == DatumExtentStatuses.Error);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Изменено datum-элементов: {ChangedCount}");
        builder.AppendLine($"Без изменений: {UnchangedCount}");
        builder.AppendLine($"Пропущено: {SkippedCount}");
        builder.AppendLine($"Ошибок: {FailedCount}");

        IReadOnlyList<DatumExtentReportRow> importantRows = Rows
            .Where(row => row.Status is DatumExtentStatuses.Error or DatumExtentStatuses.Skipped)
            .Take(8)
            .ToList();
        if (importantRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Сообщения:");
            foreach (DatumExtentReportRow row in importantRows)
            {
                builder.AppendLine($"- {row.Name}: {row.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
