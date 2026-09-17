using System.Globalization;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.Models;

public enum ParameterAuditScope
{
    Instance,
    Type
}

public enum ParameterAuditSeverity
{
    Warning,
    Error
}

public enum ParameterAuditProfileIssueSeverity
{
    Warning,
    Error
}

public enum ParameterAuditIssueCode
{
    MissingParameter,
    AmbiguousParameter,
    EmptyValue,
    UnexpectedValue,
    ValueNotAllowed,
    PatternMismatch,
    InvalidNumericValue,
    BelowMinimum,
    AboveMaximum,
    CategoryNotFound,
    NoMatchingElements,
    LinkUnavailable,
    RuleError
}

public sealed record ParameterAuditRule(
    int LineNumber,
    string RuleId,
    bool Enabled,
    string CategoryPattern,
    string FamilyPattern,
    string TypePattern,
    string ParameterName,
    Guid? ParameterGuid,
    string BuiltInParameter,
    ParameterAuditScope Scope,
    bool Required,
    string ExpectedValue,
    IReadOnlyList<string> AllowedValues,
    string ValuePattern,
    double? Minimum,
    double? Maximum,
    bool CaseSensitive,
    ParameterAuditSeverity Severity,
    string Message,
    string SelectionParameterName = "",
    string SelectionExpectedValue = "")
{
    public string ScopeDisplay => Scope == ParameterAuditScope.Type ? "Тип" : "Экземпляр";

    public string SeverityDisplay => Severity == ParameterAuditSeverity.Error ? "Ошибка" : "Предупреждение";

    public string ParameterDisplay => !string.IsNullOrWhiteSpace(ParameterName)
        ? ParameterName
        : ParameterGuid?.ToString("D") ?? BuiltInParameter;

    public bool HasSelectionFilter => !string.IsNullOrWhiteSpace(SelectionParameterName);

    public string SelectionDisplay => HasSelectionFilter
        ? $"{SelectionParameterName} = «{SelectionExpectedValue}»"
        : "По категории";

    public bool HasValueValidation => !string.IsNullOrWhiteSpace(ExpectedValue)
        || AllowedValues.Count > 0
        || !string.IsNullOrWhiteSpace(ValuePattern)
        || Minimum.HasValue
        || Maximum.HasValue;

    public string ExpectedDescription
    {
        get
        {
            List<string> parts = [];
            if (Required)
            {
                parts.Add("не пусто");
            }

            if (!string.IsNullOrWhiteSpace(ExpectedValue))
            {
                parts.Add($"равно «{ExpectedValue}»");
            }

            if (AllowedValues.Count > 0)
            {
                parts.Add($"одно из: {string.Join(" | ", AllowedValues)}");
            }

            if (!string.IsNullOrWhiteSpace(ValuePattern))
            {
                parts.Add($"формат: {ValuePattern}");
            }

            if (Minimum.HasValue || Maximum.HasValue)
            {
                string minimum = Minimum?.ToString(CultureInfo.InvariantCulture) ?? "−∞";
                string maximum = Maximum?.ToString(CultureInfo.InvariantCulture) ?? "+∞";
                parts.Add($"диапазон: {minimum}…{maximum}");
            }

            return parts.Count == 0 ? "параметр существует" : string.Join("; ", parts);
        }
    }
}

public sealed record ParameterAuditProfileIssue(
    int LineNumber,
    string RuleId,
    ParameterAuditProfileIssueSeverity Severity,
    string Message,
    string CellAddress = "",
    string Recommendation = "",
    bool CanAutoFix = false)
{
    public string SeverityDisplay => Severity == ParameterAuditProfileIssueSeverity.Error
        ? "Ошибка"
        : "Предупреждение";

    public string AutoFixDisplay => CanAutoFix ? "Да" : string.Empty;
}

public sealed record ParameterAuditProfileFix(
    int HeaderLineNumber,
    int PrimaryColumnIndex,
    int DuplicateColumnIndex,
    string ParameterName);

public sealed record ParameterAuditProfile(
    IReadOnlyList<ParameterAuditRule> Rules,
    IReadOnlyList<ParameterAuditProfileIssue> Issues,
    IReadOnlyList<ParameterAuditProfileFix>? Fixes = null)
{
    public bool IsValid => Rules.Count > 0
        && Issues.All(issue => issue.Severity != ParameterAuditProfileIssueSeverity.Error);

    public IReadOnlyList<ParameterAuditProfileFix> AvailableFixes => Fixes ?? [];
}

public sealed record ParameterAuditElementSnapshot(
    string SourceModel,
    bool IsLinked,
    long ElementId,
    string UniqueId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    ParameterAuditScope Scope,
    long? HostLinkInstanceId = null);

public sealed record ParameterAuditValueSnapshot(
    bool Exists,
    bool IsAmbiguous,
    bool HasValue,
    string DisplayValue,
    string RawValue,
    double? NumericValue)
{
    public static ParameterAuditValueSnapshot Missing { get; } = new(
        false,
        false,
        false,
        string.Empty,
        string.Empty,
        null);

    public bool IsEmpty => !HasValue
        || (string.IsNullOrWhiteSpace(DisplayValue) && string.IsNullOrWhiteSpace(RawValue));

    public string ActualValue => !string.IsNullOrWhiteSpace(DisplayValue)
        ? DisplayValue
        : RawValue;
}

public sealed record ParameterAuditResultRow(
    string RuleId,
    ParameterAuditSeverity Severity,
    ParameterAuditIssueCode IssueCode,
    string SourceModel,
    bool IsLinked,
    long? ElementId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string ParameterName,
    string ActualValue,
    string ExpectedValue,
    string Message,
    string UniqueId = "",
    long? HostLinkInstanceId = null)
{
    public string SeverityDisplay => Severity == ParameterAuditSeverity.Error ? "Ошибка" : "Предупреждение";

    public string ElementIdDisplay => ElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public string HostLinkInstanceIdDisplay => HostLinkInstanceId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    public string SourceDisplay => IsLinked ? $"Связь: {SourceModel}" : SourceModel;

    public bool CanNavigateInRevit => ElementId.HasValue
        && (!IsLinked || HostLinkInstanceId.HasValue);

    public string NavigationActionDisplay => IsLinked ? "Показать связь" : "Показать";

    public string NavigationToolTip => IsLinked
        ? "Выбрать и приблизить экземпляр RVT-связи. ElementId вложенного элемента указан в соседней колонке."
        : "Выбрать элемент, приблизить его в Revit и открыть палитру свойств.";
}

public sealed record ParameterAuditReport(
    DateTimeOffset AnalyzedAt,
    int RuleCount,
    int CheckedCount,
    int PassedCount,
    IReadOnlyList<ParameterAuditResultRow> Rows)
{
    public int ErrorCount => Rows.Count(row => row.Severity == ParameterAuditSeverity.Error);

    public int WarningCount => Rows.Count(row => row.Severity == ParameterAuditSeverity.Warning);
}
