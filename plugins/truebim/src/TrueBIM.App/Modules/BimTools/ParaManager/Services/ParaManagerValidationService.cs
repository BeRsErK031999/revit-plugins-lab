using TrueBIM.App.Modules.BimTools.ParaManager.Models;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class ParaManagerValidationService
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
        '<',
        '>',
        '?',
        '`',
        '~'
    ];

    public IReadOnlyList<ParameterImportRow> Validate(
        IReadOnlyList<ParameterImportRow> rows,
        ISet<string> existingProjectParameterNames,
        Func<string, bool> categoryExists)
    {
        HashSet<string> namesInFile = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (ParameterImportRow row in rows)
        {
            ValidateRow(row, existingProjectParameterNames, categoryExists, namesInFile);
        }

        return rows;
    }

    private static void ValidateRow(
        ParameterImportRow row,
        ISet<string> existingProjectParameterNames,
        Func<string, bool> categoryExists,
        HashSet<string> namesInFile)
    {
        if (string.IsNullOrWhiteSpace(row.ParameterName))
        {
            row.Status = ParameterImportStatus.Empty;
            row.Message = "Строка не содержит имени параметра.";
            return;
        }

        if (!IsValidName(row.ParameterName, out string nameReason))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = nameReason;
            return;
        }

        if (!namesInFile.Add(row.ParameterName))
        {
            row.Status = ParameterImportStatus.DuplicateInFile;
            row.Message = "Такое имя параметра уже есть в выбранном файле.";
            return;
        }

        if (string.IsNullOrWhiteSpace(row.SharedGroup))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = "Не указана группа shared parameters.";
            return;
        }

        if (!row.TryGetBindingKind(out _))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = "BindingType должен быть Instance или Type.";
            return;
        }

        if (row.CategoryNames.Count == 0)
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = "Не указаны категории.";
            return;
        }

        string[] missingCategories = row.CategoryNames
            .Where(categoryName => !categoryExists(categoryName))
            .ToArray();
        if (missingCategories.Length > 0)
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = $"Категории не найдены или не поддерживают project parameters: {string.Join(", ", missingCategories)}.";
            return;
        }

        if (!ParameterDataTypeResolver.IsSupported(row.DataType))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = $"Тип данных не поддержан: {row.DataType}.";
            return;
        }

        if (!ParameterGroupResolver.IsSupported(row.GroupUnder))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = $"Группа параметра не поддержана: {row.GroupUnder}.";
            return;
        }

        if (!row.TryGetVisible(out _))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = $"Visible должен быть true/false: {row.VisibleText}.";
            return;
        }

        if (!row.TryGetUserModifiable(out _))
        {
            row.Status = ParameterImportStatus.Invalid;
            row.Message = $"UserModifiable должен быть true/false: {row.UserModifiableText}.";
            return;
        }

        row.Status = existingProjectParameterNames.Contains(row.ParameterName)
            ? ParameterImportStatus.WillUpdate
            : ParameterImportStatus.WillCreate;
        row.Message = row.Status == ParameterImportStatus.WillUpdate
            ? "Параметр уже есть в проекте; будет предпринято расширение привязки категорий."
            : "Параметр будет создан в shared parameter file и привязан к проекту.";
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
