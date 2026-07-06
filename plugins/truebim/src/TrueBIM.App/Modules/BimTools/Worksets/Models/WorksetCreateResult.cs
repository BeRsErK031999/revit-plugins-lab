using System.Text;

namespace TrueBIM.App.Modules.BimTools.Worksets.Models;

public sealed record WorksetCreateResult(IReadOnlyList<WorksetImportRow> Rows)
{
    public int CreatedCount => Rows.Count(row => row.Status == WorksetImportStatus.Created);

    public int ExistingCount => Rows.Count(row => row.Status == WorksetImportStatus.Existing);

    public int SkippedCount => Rows.Count(row => row.Status is WorksetImportStatus.Empty or WorksetImportStatus.Invalid or WorksetImportStatus.DuplicateInFile);

    public int FailedCount => Rows.Count(row => row.Status == WorksetImportStatus.Failed);

    public string ToDialogText()
    {
        StringBuilder builder = new();
        builder.AppendLine("Создание рабочих наборов завершено.");
        builder.AppendLine();
        builder.AppendLine($"Создано: {CreatedCount}");
        builder.AppendLine($"Уже существовало: {ExistingCount}");
        builder.AppendLine($"Пропущено: {SkippedCount}");
        builder.AppendLine($"Ошибок: {FailedCount}");

        IReadOnlyList<WorksetImportRow> failedRows = Rows
            .Where(row => row.Status == WorksetImportStatus.Failed)
            .Take(5)
            .ToList();
        if (failedRows.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Ошибки:");
            foreach (WorksetImportRow row in failedRows)
            {
                builder.AppendLine($"- строка {row.LineNumber}: {row.WorksetName} - {row.Message}");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
