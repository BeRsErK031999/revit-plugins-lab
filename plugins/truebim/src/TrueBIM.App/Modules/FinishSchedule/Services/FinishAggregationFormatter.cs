using System.Globalization;

using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishAggregationFormatter
{
    public const string NoFinishDisplay = "—";
    public const string UnknownDisplay = "[Не удалось определить]";

    private static readonly CultureInfo OutputCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string PreservedBlankLine = "\u00A0";
    // Revit trims whitespace at the edges of string parameters. A zero-width space
    // preserves the leading/trailing alignment lines without printing a visible glyph.
    private const string PreservedAreaPadding = "\u200B";
    private readonly Func<string, int> visualLineCount;

    public FinishAggregationFormatter(
        double descriptionColumnWidthMillimeters = FinishScheduleColumnWidths.DefaultDescriptionMillimeters,
        Func<string, int>? visualLineCount = null)
    {
        if (double.IsNaN(descriptionColumnWidthMillimeters)
            || double.IsInfinity(descriptionColumnWidthMillimeters)
            || descriptionColumnWidthMillimeters < FinishScheduleColumnWidths.MinimumMillimeters
            || descriptionColumnWidthMillimeters > FinishScheduleColumnWidths.MaximumMillimeters)
            throw new ArgumentOutOfRangeException(nameof(descriptionColumnWidthMillimeters));
        this.visualLineCount = visualLineCount ?? (_ => 1);
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

        List<string> descriptionBlocks = [];
        List<string> areaBlocks = [];
        foreach (FormattedItem item in formattedItems)
        {
            // Revit wraps the unchanged description. Only area padding reflects its rendered height.
            descriptionBlocks.Add(item.Description);
            int lines = Math.Max(1, visualLineCount(item.Description));
            int areaLine = (lines - 1) / 2;
            areaBlocks.Add(string.Join(Environment.NewLine,
                Enumerable.Range(0, lines).Select(line => line == areaLine ? item.Area : PreservedAreaPadding)));
        }

        // A non-breaking space keeps the empty separator line visible in Revit text parameters.
        string separator = Environment.NewLine + PreservedBlankLine + Environment.NewLine;
        return new FinishFormattedCategoryOutput(
            string.Join(separator, descriptionBlocks),
            string.Join(separator, areaBlocks));
    }
    private sealed record FormattedItem(string Description, string Area);
}
