using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public sealed class TitleBlockValueResolver
{
    public string Resolve(Document document, ViewSheet sheet, TitleBlockParameterRule rule)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(sheet, nameof(sheet));
        Guard.NotNull(rule, nameof(rule));

        return rule.Source switch
        {
            TitleBlockValueSources.StaticText => rule.Value ?? string.Empty,
            TitleBlockValueSources.Date => FormatDate(rule.Value),
            TitleBlockValueSources.SheetNumber => sheet.SheetNumber,
            TitleBlockValueSources.SheetName => sheet.Name,
            TitleBlockValueSources.ProjectParameter => ReadParameter(document.ProjectInformation, rule.Value),
            TitleBlockValueSources.SheetParameter => ReadParameter(sheet, rule.Value),
            TitleBlockValueSources.Formula => ResolveFormula(document, sheet, rule.Value),
            _ => rule.Value ?? string.Empty
        };
    }

    private static string FormatDate(string? format)
    {
        string dateFormat = string.IsNullOrWhiteSpace(format)
            ? "dd.MM.yyyy"
            : format!.Trim();

        try
        {
            return DateTime.Today.ToString(dateFormat, System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (FormatException)
        {
            return DateTime.Today.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.CurrentCulture);
        }
    }

    private static string ReadParameter(Element? element, string? parameterName)
    {
        if (element is null || string.IsNullOrWhiteSpace(parameterName))
        {
            return string.Empty;
        }

        string trimmedParameterName = parameterName!.Trim();
        Parameter? parameter = element.LookupParameter(trimmedParameterName);
        return parameter is null
            ? string.Empty
            : parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
    }

    private static string ResolveFormula(Document document, ViewSheet sheet, string? formula)
    {
        return TitleBlockFormulaEvaluator.Evaluate(formula, token => ResolveFormulaToken(document, sheet, token));
    }

    private static string ResolveFormulaToken(Document document, ViewSheet sheet, string token)
    {
        (string name, string? value) = SplitToken(token);
        if (string.Equals(name, "SheetNumber", StringComparison.OrdinalIgnoreCase))
        {
            return sheet.SheetNumber;
        }

        if (string.Equals(name, "SheetName", StringComparison.OrdinalIgnoreCase))
        {
            return sheet.Name;
        }

        if (string.Equals(name, "Date", StringComparison.OrdinalIgnoreCase))
        {
            return FormatDate(value);
        }

        if (string.Equals(name, "ProjectParameter", StringComparison.OrdinalIgnoreCase))
        {
            return ReadParameter(document.ProjectInformation, value);
        }

        if (string.Equals(name, "SheetParameter", StringComparison.OrdinalIgnoreCase))
        {
            return ReadParameter(sheet, value);
        }

        return string.Empty;
    }

    private static (string Name, string? Value) SplitToken(string token)
    {
        int separatorIndex = token.IndexOf(':');
        if (separatorIndex < 0)
        {
            return (token.Trim(), null);
        }

        return (
            token.Substring(0, separatorIndex).Trim(),
            token.Substring(separatorIndex + 1).Trim());
    }
}
