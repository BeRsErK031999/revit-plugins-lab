using System.Text;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;

public sealed record MepDimensionApplyResult(IReadOnlyList<MepDimensionReportRow> Rows)
{
    public int CreatedCount => Rows.Count(row => row.Status == MepDimensionStatuses.Done);

    public int SkippedCount => Rows.Count(row => row.Status == MepDimensionStatuses.Skipped);

    public int FailedCount => Rows.Count(row => row.Status == MepDimensionStatuses.Error);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Создано размерных цепочек: {CreatedCount}");
        builder.AppendLine($"Пропущено: {SkippedCount}");
        builder.AppendLine($"Ошибок: {FailedCount}");

        IReadOnlyList<MepDimensionReportRow> importantRows = Rows
            .Where(row => row.Status != MepDimensionStatuses.Done)
            .Take(8)
            .ToList();
        if (importantRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Сообщения:");
            foreach (MepDimensionReportRow row in importantRows)
            {
                builder.AppendLine($"- {row.CandidateId}: {row.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
