using System.Globalization;
using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishAggregationFormatter
{
    public const string NoFinishDisplay = "—";
    public const string UnknownDisplay = "[Не удалось определить]";

    private static readonly CultureInfo OutputCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly string ItemSeparator = Environment.NewLine + Environment.NewLine;

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
        List<string> descriptions = items
            .Select(item => item.Description.DisplayValue)
            .ToList();
        List<string> areas = items
            .Select(item => item.AreaSquareMeters.ToString("N2", OutputCulture))
            .ToList();
        if (category.State == FinishValueState.Unknown)
        {
            descriptions.Add(UnknownDisplay);
            areas.Add(UnknownDisplay);
        }

        return new FinishFormattedCategoryOutput(
            string.Join(ItemSeparator, descriptions),
            string.Join(ItemSeparator, areas));
    }
}
