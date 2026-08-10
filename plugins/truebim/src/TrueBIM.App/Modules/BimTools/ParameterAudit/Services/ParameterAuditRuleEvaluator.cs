using System.Globalization;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.Services;

public sealed class ParameterAuditRuleEvaluator
{
    public bool MatchesTarget(ParameterAuditRule rule, ParameterAuditElementSnapshot element)
    {
        return rule.Scope == element.Scope
            && MatchesSelector(element.CategoryName, rule.CategoryPattern)
            && MatchesSelector(element.FamilyName, rule.FamilyPattern)
            && MatchesSelector(element.TypeName, rule.TypePattern);
    }

    public bool MatchesSelection(
        ParameterAuditRule rule,
        ParameterAuditValueSnapshot selectionValue)
    {
        return !rule.HasSelectionFilter
            || (selectionValue.Exists
                && !selectionValue.IsEmpty
                && MatchesAnyValue(selectionValue, rule.SelectionExpectedValue, false));
    }

    public ParameterAuditResultRow? Evaluate(
        ParameterAuditRule rule,
        ParameterAuditElementSnapshot element,
        ParameterAuditValueSnapshot value)
    {
        if (!rule.Enabled || !MatchesTarget(rule, element))
        {
            return null;
        }

        if (value.IsAmbiguous)
        {
            return CreateResult(
                rule,
                element,
                value,
                ParameterAuditIssueCode.AmbiguousParameter,
                "У элемента найдено несколько параметров с таким именем. Укажите GUID или BuiltInParameter.");
        }

        if (!value.Exists)
        {
            return rule.Required || rule.HasValueValidation
                ? CreateResult(
                    rule,
                    element,
                    value,
                    ParameterAuditIssueCode.MissingParameter,
                    "Параметр отсутствует у элемента.")
                : null;
        }

        if (value.IsEmpty)
        {
            return rule.Required
                ? CreateResult(
                    rule,
                    element,
                    value,
                    ParameterAuditIssueCode.EmptyValue,
                    "Параметр не заполнен или содержит только пробелы.")
                : null;
        }

        if (!string.IsNullOrWhiteSpace(rule.ExpectedValue)
            && !MatchesAnyValue(value, rule.ExpectedValue, rule.CaseSensitive))
        {
            return CreateResult(
                rule,
                element,
                value,
                ParameterAuditIssueCode.UnexpectedValue,
                $"Ожидалось значение «{rule.ExpectedValue}». ");
        }

        if (rule.AllowedValues.Count > 0
            && !rule.AllowedValues.Any(allowed => MatchesAnyValue(value, allowed, rule.CaseSensitive)))
        {
            return CreateResult(
                rule,
                element,
                value,
                ParameterAuditIssueCode.ValueNotAllowed,
                $"Значение не входит в допустимый список: {string.Join(" | ", rule.AllowedValues)}.");
        }

        if (!string.IsNullOrWhiteSpace(rule.ValuePattern)
            && !Regex.IsMatch(
                value.ActualValue,
                rule.ValuePattern,
                rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
        {
            return CreateResult(
                rule,
                element,
                value,
                ParameterAuditIssueCode.PatternMismatch,
                $"Значение не соответствует формату «{rule.ValuePattern}». ");
        }

        if (rule.Minimum.HasValue || rule.Maximum.HasValue)
        {
            if (!TryGetNumericValue(value, out double number))
            {
                return CreateResult(
                    rule,
                    element,
                    value,
                    ParameterAuditIssueCode.InvalidNumericValue,
                    "Значение нельзя проверить как число.");
            }

            if (rule.Minimum.HasValue && number < rule.Minimum.Value)
            {
                return CreateResult(
                    rule,
                    element,
                    value,
                    ParameterAuditIssueCode.BelowMinimum,
                    $"Значение {FormatNumber(number)} меньше минимума {FormatNumber(rule.Minimum.Value)}.");
            }

            if (rule.Maximum.HasValue && number > rule.Maximum.Value)
            {
                return CreateResult(
                    rule,
                    element,
                    value,
                    ParameterAuditIssueCode.AboveMaximum,
                    $"Значение {FormatNumber(number)} больше максимума {FormatNumber(rule.Maximum.Value)}.");
            }
        }

        return null;
    }

    public bool MatchesSelector(string value, string selector)
    {
        string normalizedSelector = string.IsNullOrWhiteSpace(selector) ? "*" : selector.Trim();
        if (normalizedSelector == "*")
        {
            return true;
        }

        string expression = "^" + Regex.Escape(normalizedSelector)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(value ?? string.Empty, expression, RegexOptions.IgnoreCase);
    }

    private static bool MatchesAnyValue(
        ParameterAuditValueSnapshot value,
        string expected,
        bool caseSensitive)
    {
        StringComparison comparison = caseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return string.Equals(value.DisplayValue.Trim(), expected.Trim(), comparison)
            || string.Equals(value.RawValue.Trim(), expected.Trim(), comparison);
    }

    private static bool TryGetNumericValue(ParameterAuditValueSnapshot value, out double number)
    {
        if (value.NumericValue.HasValue)
        {
            number = value.NumericValue.Value;
            return true;
        }

        string[] candidates = [value.DisplayValue, value.RawValue];
        foreach (string candidate in candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            string normalized = candidate.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty);
            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out number)
                || double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                || double.TryParse(normalized.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                return true;
            }
        }

        number = 0;
        return false;
    }

    private static ParameterAuditResultRow CreateResult(
        ParameterAuditRule rule,
        ParameterAuditElementSnapshot element,
        ParameterAuditValueSnapshot value,
        ParameterAuditIssueCode code,
        string reason)
    {
        string message = string.IsNullOrWhiteSpace(rule.Message)
            ? reason.Trim()
            : $"{reason.Trim()} {rule.Message}";
        return new ParameterAuditResultRow(
            rule.RuleId,
            rule.Severity,
            code,
            element.SourceModel,
            element.IsLinked,
            element.ElementId,
            element.CategoryName,
            element.FamilyName,
            element.TypeName,
            rule.ParameterDisplay,
            value.ActualValue,
            rule.ExpectedDescription,
            message);
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###############", CultureInfo.CurrentCulture);
    }
}
