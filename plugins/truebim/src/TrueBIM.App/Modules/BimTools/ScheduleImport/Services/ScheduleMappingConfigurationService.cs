using System.Security.Cryptography;
using System.Text;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleMappingConfigurationService
{
    public ScheduleMappingValidationResult Validate(
        ParsedTable table,
        long? categoryId,
        IReadOnlyList<ScheduleFieldMapping> mappings)
    {
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(mappings, nameof(mappings));

        List<string> warnings = [];
        List<string> errors = [];
        if (categoryId is null)
        {
            errors.Add("Выберите категорию Revit для новой спецификации.");
        }

        if (mappings.Count == 0)
        {
            errors.Add("Выберите хотя бы одну колонку PDF и сопоставьте её с полем Revit.");
        }

        foreach (ScheduleFieldMapping mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.SourceColumnName))
            {
                errors.Add("Найдена колонка PDF без названия.");
            }

            if (string.IsNullOrWhiteSpace(mapping.TargetFieldKey)
                || string.IsNullOrWhiteSpace(mapping.TargetFieldName))
            {
                errors.Add($"Для колонки «{mapping.SourceColumnName}» не выбрано поле Revit.");
            }

            if (RequiresValue(mapping.FilterRule) && string.IsNullOrWhiteSpace(mapping.FilterValue))
            {
                errors.Add($"Для условия колонки «{mapping.SourceColumnName}» укажите значение.");
            }
        }

        IReadOnlyList<string> duplicateFields = mappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.TargetFieldKey))
            .GroupBy(mapping => mapping.TargetFieldKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.First().TargetFieldName)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (duplicateFields.Count > 0)
        {
            errors.Add($"Одно поле Revit нельзя добавить дважды: {string.Join(", ", duplicateFields)}.");
        }

        int ignoredColumnCount = Math.Max(0, table.ColumnCount - mappings.Count);
        if (ignoredColumnCount > 0)
        {
            warnings.Add($"Не будут добавлены в спецификацию колонок PDF: {ignoredColumnCount}.");
        }

        string fingerprint = categoryId is null
            ? string.Empty
            : CreateFingerprint(table, categoryId.Value, mappings);
        return new ScheduleMappingValidationResult(
            fingerprint,
            warnings,
            errors.Distinct(StringComparer.CurrentCulture).ToList());
    }

    public string CreateFingerprint(
        ParsedTable table,
        long categoryId,
        IReadOnlyList<ScheduleFieldMapping> mappings)
    {
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(mappings, nameof(mappings));

        StringBuilder builder = new();
        builder.Append(categoryId)
            .Append('|')
            .Append(table.SourceFilePath)
            .Append('|')
            .Append(table.PageNumber);
        foreach (ScheduleFieldMapping mapping in mappings)
        {
            builder.Append('\n')
                .Append(mapping.SourceColumnName)
                .Append('|')
                .Append(mapping.TargetFieldKey)
                .Append('|')
                .Append(mapping.FilterRule)
                .Append('|')
                .Append(mapping.FilterValue?.Trim() ?? string.Empty);
        }

        using SHA256 sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    public static bool RequiresValue(ScheduleFilterRule rule)
    {
        return rule is not ScheduleFilterRule.None
            and not ScheduleFilterRule.HasValue
            and not ScheduleFilterRule.HasNoValue;
    }
}
