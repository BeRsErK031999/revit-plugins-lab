using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.Print.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;

public sealed class ScheduleRegisterAggregationService
{
    public ScheduleRegisterAggregationResult Aggregate(IReadOnlyList<SchedulePlacementSnapshot> placements)
    {
        Guard.NotNull(placements, nameof(placements));
        List<string> warnings = [];
        List<ScheduleRegisterRow> rows = [];

        foreach (IGrouping<long, SchedulePlacementSnapshot> scheduleGroup in placements
                     .GroupBy(placement => placement.ScheduleId))
        {
            SchedulePlacementSnapshot first = scheduleGroup.First();
            SchedulePlacementSnapshot[] uniquePlacements = scheduleGroup
                .GroupBy(placement => (placement.SheetId, placement.SegmentIndex))
                .Select(group => group.First())
                .ToArray();
            string[] sheetNumbers = uniquePlacements
                .Select(placement => placement.SheetNumber)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, PrintSheetNumberComparer.Instance)
                .ToArray();

            bool repeatedSegmentAcrossSheets = uniquePlacements
                .GroupBy(placement => placement.SegmentIndex)
                .Any(group => group.Select(item => item.SheetId).Distinct().Count() > 1);
            if (repeatedSegmentAcrossSheets)
            {
                warnings.Add(
                    $"Спецификация «{first.ScheduleBrowserName}» размещена повторно на листах "
                    + $"{string.Join(", ", sheetNumbers)}. Проверьте размещения в диспетчере проекта.");
            }

            rows.Add(new ScheduleRegisterRow(
                first.ScheduleId,
                string.Join(", ", sheetNumbers),
                first.ScheduleTitle,
                first.ScheduleBrowserName));
        }

        foreach (IGrouping<string, ScheduleRegisterRow> titleGroup in rows
                     .GroupBy(row => NormalizeTitle(row.ScheduleTitle), StringComparer.CurrentCultureIgnoreCase)
                     .Where(group => group.Select(row => row.ScheduleId).Distinct().Count() > 1))
        {
            string details = string.Join(
                "; ",
                titleGroup
                    .OrderBy(row => row.SheetNumbers, PrintSheetNumberComparer.Instance)
                    .Select(row => $"«{row.ScheduleBrowserName}» — листы {row.SheetNumbers}"));
            warnings.Add(
                $"Одинаковый заголовок «{titleGroup.First().ScheduleTitle}» найден у разных спецификаций: {details}.");
        }

        ScheduleRegisterRow[] orderedRows = rows
            .OrderBy(row => FirstSheetNumber(row.SheetNumbers), PrintSheetNumberComparer.Instance)
            .ThenBy(row => row.ScheduleTitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.ScheduleId)
            .ToArray();
        return new ScheduleRegisterAggregationResult(orderedRows, warnings.Distinct().ToArray());
    }

    private static string NormalizeTitle(string value)
    {
        return string.Join(
            " ",
            value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string FirstSheetNumber(string sheetNumbers)
    {
        int separator = sheetNumbers.IndexOf(',');
        return separator < 0 ? sheetNumbers : sheetNumbers.Substring(0, separator).Trim();
    }
}
