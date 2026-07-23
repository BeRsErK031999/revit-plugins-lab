using System.Globalization;
using System.Text;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishAggregationFormatter
{
    public const string NoFinishDisplay = "—";
    public const string UnknownDisplay = "[Не удалось определить]";

    private static readonly CultureInfo OutputCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string PreservedBlankLine = "\u00A0";
    private const double HorizontalCellPaddingMillimeters = 2;
    private const double AverageGlyphWidthFactor = 0.58;
    private readonly int maximumCharactersPerLine;

    public FinishAggregationFormatter(
        double descriptionColumnWidthMillimeters =
            FinishScheduleColumnWidths.DefaultDescriptionMillimeters)
    {
        if (double.IsNaN(descriptionColumnWidthMillimeters)
            || double.IsInfinity(descriptionColumnWidthMillimeters)
            || descriptionColumnWidthMillimeters < FinishScheduleColumnWidths.MinimumMillimeters
            || descriptionColumnWidthMillimeters > FinishScheduleColumnWidths.MaximumMillimeters)
        {
            throw new ArgumentOutOfRangeException(nameof(descriptionColumnWidthMillimeters));
        }

        double usableWidth = Math.Max(
            FinishRoomScheduleStyleRules.BodyTextSizeMillimeters * 4,
            descriptionColumnWidthMillimeters - HorizontalCellPaddingMillimeters);
        maximumCharactersPerLine = Math.Max(
            8,
            (int)Math.Floor(
                usableWidth
                / (FinishRoomScheduleStyleRules.BodyTextSizeMillimeters
                    * AverageGlyphWidthFactor)));
    }

    public FinishRoomGroupOutput Format(
        IEnumerable<string> roomIdentifiers,
        FinishAggregatedCategory walls,
        FinishAggregatedCategory floors,
        FinishAggregatedCategory ceilings)
    {
        if (roomIdentifiers is null)
        {
            throw new ArgumentNullException(nameof(roomIdentifiers));
        }

        string roomList = string.Join(", ", roomIdentifiers
            .OrderBy(identifier => identifier, NaturalStringComparer.Instance));
        return new FinishRoomGroupOutput(
            roomList,
            FormatCategory(walls),
            FormatCategory(floors),
            FormatCategory(ceilings));
    }

    public FinishFormattedCategoryOutput? FormatCategory(FinishAggregatedCategory category)
    {
        if (category is null)
        {
            throw new ArgumentNullException(nameof(category));
        }

        if (!category.IsEnabled)
        {
            return null;
        }

        if (category.Items.Count == 0)
        {
            string value = category.State == FinishValueState.Unknown
                ? UnknownDisplay
                : NoFinishDisplay;
            return new FinishFormattedCategoryOutput(value, value);
        }

        FinishAggregatedItem[] items = category.Items
            .OrderBy(item => item.Description.DisplayValue, NaturalStringComparer.Instance)
            .ToArray();
        List<FormattedItem> formattedItems = items
            .Select(item => new FormattedItem(
                item.Description.DisplayValue,
                item.AreaSquareMeters.ToString("N2", OutputCulture)))
            .ToList();
        if (category.State == FinishValueState.Unknown)
        {
            formattedItems.Add(new FormattedItem(UnknownDisplay, UnknownDisplay));
        }

        List<string> descriptionLines = [];
        List<string> areaLines = [];
        for (int index = 0; index < formattedItems.Count; index++)
        {
            if (index > 0)
            {
                descriptionLines.Add(string.Empty);
                areaLines.Add(PreservedBlankLine);
            }

            FormattedItem item = formattedItems[index];
            IReadOnlyList<string> wrappedDescription = Wrap(item.Description);
            descriptionLines.AddRange(wrappedDescription);
            int areaLine = (wrappedDescription.Count - 1) / 2;
            for (int line = 0; line < wrappedDescription.Count; line++)
            {
                areaLines.Add(line == areaLine ? item.Area : PreservedBlankLine);
            }
        }

        return new FinishFormattedCategoryOutput(
            string.Join(Environment.NewLine, descriptionLines),
            string.Join(Environment.NewLine, areaLines));
    }

    private IReadOnlyList<string> Wrap(string value)
    {
        string[] words = value.Split(
            [' '],
            StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [string.Empty];
        }

        List<string> lines = [];
        StringBuilder current = new();
        foreach (string sourceWord in words)
        {
            string word = sourceWord;
            while (word.Length > maximumCharactersPerLine)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }

                lines.Add(word.Substring(0, maximumCharactersPerLine));
                word = word.Substring(maximumCharactersPerLine);
            }

            if (word.Length == 0)
            {
                continue;
            }

            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= maximumCharactersPerLine)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return lines;
    }

    private sealed record FormattedItem(string Description, string Area);
}
