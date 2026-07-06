using TrueBIM.App.Modules.BimTools.Worksets.Models;

namespace TrueBIM.App.Modules.BimTools.Worksets.Services;

public sealed class WorksetValidationService
{
    private static readonly char[] InvalidNameCharacters =
    [
        '\\',
        '/',
        ':',
        '{',
        '}',
        '[',
        ']',
        '|',
        ';',
        '<',
        '>',
        '?',
        '`',
        '~'
    ];

    public IReadOnlyList<WorksetImportRow> Validate(
        IReadOnlyList<WorksetImportRow> rows,
        ISet<string> existingWorksetNames)
    {
        HashSet<string> namesInFile = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (WorksetImportRow row in rows)
        {
            ValidateRow(row, existingWorksetNames, namesInFile);
        }

        return rows;
    }

    private static void ValidateRow(
        WorksetImportRow row,
        ISet<string> existingWorksetNames,
        HashSet<string> namesInFile)
    {
        if (string.IsNullOrWhiteSpace(row.WorksetName))
        {
            row.Status = WorksetImportStatus.Empty;
            row.Message = "Строка не содержит имени рабочего набора.";
            return;
        }

        if (!IsValidName(row.WorksetName, out string reason))
        {
            row.Status = WorksetImportStatus.Invalid;
            row.Message = reason;
            return;
        }

        if (!namesInFile.Add(row.WorksetName))
        {
            row.Status = WorksetImportStatus.DuplicateInFile;
            row.Message = "Такое имя уже есть в выбранном файле.";
            return;
        }

        if (existingWorksetNames.Contains(row.WorksetName))
        {
            row.Status = WorksetImportStatus.Existing;
            row.Message = "Рабочий набор уже существует в модели.";
            return;
        }

        row.Status = WorksetImportStatus.WillCreate;
        row.Message = "Рабочий набор будет создан.";
    }

    private static bool IsValidName(string name, out string reason)
    {
        if (name.Length > 128)
        {
            reason = "Имя длиннее 128 символов.";
            return false;
        }

        if (name.IndexOfAny(InvalidNameCharacters) >= 0)
        {
            reason = "Имя содержит запрещённые символы.";
            return false;
        }

        if (name.Any(char.IsControl))
        {
            reason = "Имя содержит управляющие символы.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
