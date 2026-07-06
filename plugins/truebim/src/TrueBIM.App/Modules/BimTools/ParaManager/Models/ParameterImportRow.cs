namespace TrueBIM.App.Modules.BimTools.ParaManager.Models;

public sealed class ParameterImportRow
{
    public ParameterImportRow(
        int lineNumber,
        string parameterName,
        string sharedGroup,
        string bindingType,
        string categories,
        string groupUnder,
        string dataType,
        string visible,
        string userModifiable,
        string description)
    {
        LineNumber = lineNumber;
        ParameterName = Normalize(parameterName);
        SharedGroup = string.IsNullOrWhiteSpace(sharedGroup) ? "BIM" : Normalize(sharedGroup);
        BindingType = string.IsNullOrWhiteSpace(bindingType) ? "Instance" : Normalize(bindingType);
        Categories = Normalize(categories);
        GroupUnder = string.IsNullOrWhiteSpace(groupUnder) ? "Identity Data" : Normalize(groupUnder);
        DataType = string.IsNullOrWhiteSpace(dataType) ? "Text" : Normalize(dataType);
        VisibleText = string.IsNullOrWhiteSpace(visible) ? "true" : Normalize(visible);
        UserModifiableText = string.IsNullOrWhiteSpace(userModifiable) ? "true" : Normalize(userModifiable);
        Description = description.Trim();
        Status = ParameterImportStatus.Empty;
        Message = string.Empty;
    }

    public int LineNumber { get; }

    public string ParameterName { get; }

    public string SharedGroup { get; }

    public string BindingType { get; }

    public string Categories { get; }

    public string GroupUnder { get; }

    public string DataType { get; }

    public string VisibleText { get; }

    public string UserModifiableText { get; }

    public string Description { get; }

    public ParameterImportStatus Status { get; set; }

    public string Message { get; set; }

    public bool CanApply => Status is ParameterImportStatus.WillCreate or ParameterImportStatus.WillUpdate;

    public string StatusDisplay => Status switch
    {
        ParameterImportStatus.WillCreate => "Будет создан",
        ParameterImportStatus.WillUpdate => "Будет обновлён",
        ParameterImportStatus.Created => "Создан",
        ParameterImportStatus.Updated => "Обновлён",
        ParameterImportStatus.Empty => "Пустая строка",
        ParameterImportStatus.Invalid => "Ошибка проверки",
        ParameterImportStatus.DuplicateInFile => "Дубликат в файле",
        ParameterImportStatus.Skipped => "Пропущен",
        ParameterImportStatus.Failed => "Ошибка",
        _ => Status.ToString()
    };

    public IReadOnlyList<string> CategoryNames => SplitCategories(Categories);

    public ParameterImportRow WithCategories(string categories)
    {
        return new ParameterImportRow(
            LineNumber,
            ParameterName,
            SharedGroup,
            BindingType,
            categories,
            GroupUnder,
            DataType,
            VisibleText,
            UserModifiableText,
            Description)
        {
            Status = Status,
            Message = Message
        };
    }

    public bool TryGetBindingKind(out ParameterBindingKind bindingKind)
    {
        string normalized = BindingType.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (string.Equals(normalized, "Instance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Экземпляр", StringComparison.CurrentCultureIgnoreCase))
        {
            bindingKind = ParameterBindingKind.Instance;
            return true;
        }

        if (string.Equals(normalized, "Type", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Тип", StringComparison.CurrentCultureIgnoreCase))
        {
            bindingKind = ParameterBindingKind.Type;
            return true;
        }

        bindingKind = ParameterBindingKind.Instance;
        return false;
    }

    public bool TryGetVisible(out bool visible)
    {
        return TryParseBool(VisibleText, out visible);
    }

    public bool TryGetUserModifiable(out bool userModifiable)
    {
        return TryParseBool(UserModifiableText, out userModifiable);
    }

    private static string Normalize(string value)
    {
        string normalized = value.Trim();
        while (normalized.IndexOf("  ", StringComparison.Ordinal) >= 0)
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized;
    }

    private static IReadOnlyList<string> SplitCategories(string categories)
    {
        return categories
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(category => Normalize(category))
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool TryParseBool(string value, out bool result)
    {
        string normalized = value.Trim();
        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1", StringComparison.Ordinal)
            || string.Equals(normalized, "да", StringComparison.CurrentCultureIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "0", StringComparison.Ordinal)
            || string.Equals(normalized, "нет", StringComparison.CurrentCultureIgnoreCase))
        {
            result = false;
            return true;
        }

        result = true;
        return false;
    }
}
