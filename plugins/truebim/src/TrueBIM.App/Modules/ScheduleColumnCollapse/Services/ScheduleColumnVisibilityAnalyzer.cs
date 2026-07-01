using System.Globalization;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Services;

public sealed class ScheduleColumnVisibilityAnalyzer
{
    public IReadOnlyList<ScheduleColumnVisibilityDecision> Analyze(IEnumerable<ScheduleColumnState> columns)
    {
        Guard.NotNull(columns, nameof(columns));

        return columns.Select(AnalyzeColumn).ToList();
    }

    public ScheduleColumnVisibilityDecision AnalyzeColumn(ScheduleColumnState column)
    {
        Guard.NotNull(column, nameof(column));

        if (!column.CanHide)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Keep,
                "Поле нельзя скрывать через API Revit.");
        }

        NumericColumnSummary summary = SummarizeNumericValues(column);
        if (!summary.HasNumericValues)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Show,
                "Колонка не выглядит числовой, поэтому оставлена видимой.");
        }

        if (summary.HasNonZeroValues)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Show,
                "В колонке есть ненулевые значения.");
        }

        return new ScheduleColumnVisibilityDecision(
            column.FieldName,
            ScheduleColumnVisibilityAction.Hide,
            "Все числовые значения в колонке равны нулю.");
    }

    private static NumericColumnSummary SummarizeNumericValues(ScheduleColumnState column)
    {
        bool hasNumericValues = false;
        bool hasNonZeroValues = false;

        foreach (string cellText in column.CellTexts)
        {
            if (IsColumnLabel(cellText, column))
            {
                continue;
            }

            if (!TryParseDisplayedNumber(cellText, out decimal value))
            {
                continue;
            }

            hasNumericValues = true;
            if (value != decimal.Zero)
            {
                hasNonZeroValues = true;
            }
        }

        return new NumericColumnSummary(hasNumericValues, hasNonZeroValues);
    }

    private static bool IsColumnLabel(string? text, ScheduleColumnState column)
    {
        return TextEquals(text, column.ColumnHeading) || TextEquals(text, column.FieldName);
    }

    private static bool TextEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            NormalizeTextForComparison(left!),
            NormalizeTextForComparison(right!),
            StringComparison.CurrentCultureIgnoreCase);
    }

    private static string NormalizeTextForComparison(string text)
    {
        return text
            .Trim()
            .Replace('\u00a0', ' ')
            .Replace('\u2212', '-');
    }

    private static bool TryParseDisplayedNumber(string? text, out decimal value)
    {
        value = decimal.Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text!
            .Trim()
            .Replace('\u00a0', ' ')
            .Replace(" ", string.Empty)
            .Replace(',', '.')
            .Replace('\u2212', '-');

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private sealed record NumericColumnSummary(bool HasNumericValues, bool HasNonZeroValues);
}
