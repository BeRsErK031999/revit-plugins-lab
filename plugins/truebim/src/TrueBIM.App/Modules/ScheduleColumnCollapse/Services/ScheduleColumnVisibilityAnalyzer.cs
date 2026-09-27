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
                "Видимость служебного или недоступного для изменения поля сохранена.");
        }

        if (!column.IsNumeric)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Keep,
                "Видимость текстовых и служебных полей не изменяется.");
        }

        NumericColumnSummary summary = SummarizeNumericValues(column);
        if (summary.HasNonZeroValues)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Show,
                "В колонке есть ненулевые значения.");
        }

        if (summary.HasUnparsedValues)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Keep,
                "Есть нераспознанные значения; исходная видимость сохранена.");
        }

        return new ScheduleColumnVisibilityDecision(
            column.FieldName,
            ScheduleColumnVisibilityAction.Hide,
            "В числовой колонке только нули или пустые ячейки.");
    }

    private static NumericColumnSummary SummarizeNumericValues(ScheduleColumnState column)
    {
        bool hasNonZeroValues = false;
        bool hasUnparsedValues = false;

        for (int index = 0; index < column.CellTexts.Count; index++)
        {
            string cellText = column.CellTexts[index];
            if (IsColumnLabel(cellText, column) || IsEmptyValue(cellText))
            {
                continue;
            }

            double? parsedValue = column.ParsedNumericValues?[index];
            if (parsedValue.HasValue)
            {
                hasNonZeroValues |= parsedValue.Value != 0;
                continue;
            }

            if (TryParseDisplayedNumber(cellText, out decimal value))
            {
                hasNonZeroValues |= value != decimal.Zero;
            }
            else
            {
                hasUnparsedValues = true;
            }
        }

        return new NumericColumnSummary(hasNonZeroValues, hasUnparsedValues);
    }

    private static bool IsEmptyValue(string? text)
    {
        return string.IsNullOrWhiteSpace(text) || text!.Trim() is "-" or "–" or "—";
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
            .Replace('\u202f', ' ')
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
            .Replace('\u202f', ' ')
            .Replace(" ", string.Empty)
            .Replace(',', '.')
            .Replace('\u2212', '-');

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private sealed record NumericColumnSummary(bool HasNonZeroValues, bool HasUnparsedValues);
}
