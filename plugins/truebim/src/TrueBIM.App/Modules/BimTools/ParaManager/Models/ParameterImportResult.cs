using System.Text;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Models;

public sealed class ParameterImportResult
{
    public ParameterImportResult(IReadOnlyList<ParameterImportRow> rows)
    {
        Rows = rows ?? [];
    }

    public IReadOnlyList<ParameterImportRow> Rows { get; }

    public int CreatedCount => Rows.Count(row => row.Status == ParameterImportStatus.Created);

    public int UpdatedCount => Rows.Count(row => row.Status == ParameterImportStatus.Updated);

    public int SkippedCount => Rows.Count(row => row.Status == ParameterImportStatus.Skipped);

    public int FailedCount => Rows.Count(row => row.Status == ParameterImportStatus.Failed);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine($"Создано параметров: {CreatedCount}");
        builder.AppendLine($"Обновлено привязок: {UpdatedCount}");
        builder.AppendLine($"Пропущено: {SkippedCount}");
        builder.AppendLine($"Ошибок: {FailedCount}");

        IReadOnlyList<ParameterImportRow> failedRows = Rows
            .Where(row => row.Status == ParameterImportStatus.Failed)
            .Take(8)
            .ToList();
        if (failedRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Ошибки:");
            foreach (ParameterImportRow row in failedRows)
            {
                builder.AppendLine($"- строка {row.LineNumber}: {row.ParameterName} - {row.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
