using System.Globalization;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Services;

public sealed class ScheduleColumnVisibilityAnalyzer
{
    private static readonly string[] AlwaysVisibleTokens =
    [
        "итого",
        "total"
    ];

    public IReadOnlyList<ScheduleColumnVisibilityDecision> Analyze(IEnumerable<ScheduleColumnState> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        return columns.Select(AnalyzeColumn).ToList();
    }

    public ScheduleColumnVisibilityDecision AnalyzeColumn(ScheduleColumnState column)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (!column.CanHide)
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Keep,
                "Поле нельзя скрывать через API Revit.");
        }

        if (IsAlwaysVisibleColumn(column))
        {
            return new ScheduleColumnVisibilityDecision(
                column.FieldName,
                ScheduleColumnVisibilityAction.Show,
                "Итоговая колонка должна оставаться видимой.");
        }

        NumericColumnSummary summary = SummarizeNumericValues(column.CellTexts);
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

    private static bool IsAlwaysVisibleColumn(ScheduleColumnState column)
    {
        return ContainsAlwaysVisibleToken(column.FieldName) || ContainsAlwaysVisibleToken(column.ColumnHeading);
    }

    private static bool ContainsAlwaysVisibleToken(string text)
    {
        return AlwaysVisibleTokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static NumericColumnSummary SummarizeNumericValues(IEnumerable<string> cellTexts)
    {
        bool hasNumericValues = false;
        bool hasNonZeroValues = false;

        foreach (string cellText in cellTexts)
        {
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

    private static bool TryParseDisplayedNumber(string? text, out decimal value)
    {
        value = decimal.Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = text
            .Trim()
            .Replace('\u00a0', ' ')
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.')
            .Replace('−', '-');

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    private sealed record NumericColumnSummary(bool HasNumericValues, bool HasNonZeroValues);
}
