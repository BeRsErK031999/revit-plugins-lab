using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;

public sealed class SchedulePlacementCollector
{
    public ScheduleRegisterCollectionResult Collect(
        Document document,
        IReadOnlyCollection<ElementId> selectedSheetIds,
        ScheduleRegisterSettings settings)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(selectedSheetIds, nameof(selectedSheetIds));
        settings = ScheduleRegisterSettingsStorage.Normalize(settings);

        HashSet<ElementId> sheetIds = new(selectedSheetIds);
        Dictionary<ElementId, ViewSheet> sheets = sheetIds
            .Select(document.GetElement)
            .OfType<ViewSheet>()
            .ToDictionary(sheet => sheet.Id);
        List<SchedulePlacementSnapshot> placements = [];
        List<string> warnings = [];
        HashSet<ElementId> missingParameterScheduleIds = [];
        HashSet<ElementId> excludedScheduleIds = [];

        foreach (ScheduleSheetInstance instance in new FilteredElementCollector(document)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .Cast<ScheduleSheetInstance>())
        {
            if (instance.IsTitleblockRevisionSchedule
                || !sheets.TryGetValue(instance.OwnerViewId, out ViewSheet? sheet)
                || document.GetElement(instance.ScheduleId) is not ViewSchedule schedule
                || ScheduleRegisterNameService.IsTemplateOrGeneratedCopy(schedule.Name))
            {
                continue;
            }

            if (settings.FilterEnabled)
            {
                Parameter[] parameters = schedule.GetParameters(settings.FilterParameterName).ToArray();
                if (parameters.Length == 0)
                {
                    missingParameterScheduleIds.Add(schedule.Id);
                }
                else if (parameters.Any(parameter => ContainsExcludedValue(parameter, settings.ExcludedValue)))
                {
                    excludedScheduleIds.Add(schedule.Id);
                    continue;
                }
            }

            string title = ReadHeaderTitle(schedule);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "[Без заголовка]";
                warnings.Add(
                    $"У спецификации «{schedule.Name}» на листе {sheet.SheetNumber} пустая первая строка шапки.");
            }

            placements.Add(new SchedulePlacementSnapshot(
                RevitElementIds.GetValue(schedule.Id),
                schedule.Name,
                title,
                RevitElementIds.GetValue(sheet.Id),
                sheet.SheetNumber,
                GetSegmentIndex(instance)));
        }

        if (missingParameterScheduleIds.Count > 0)
        {
            warnings.Add(
                $"Параметр «{settings.FilterParameterName}» отсутствует у спецификаций: "
                + string.Join(
                    ", ",
                    missingParameterScheduleIds
                        .Select(document.GetElement)
                        .OfType<ViewSchedule>()
                        .Select(schedule => $"«{schedule.Name}»"))
                + ". Они включены в ведомость.");
        }

        return new ScheduleRegisterCollectionResult(
            placements,
            excludedScheduleIds.Count,
            warnings.Distinct().ToArray());
    }

    public IReadOnlyList<string> CollectParameterNames(Document document)
    {
        Guard.NotNull(document, nameof(document));
        SortedSet<string> names = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (ViewSchedule schedule in new FilteredElementCollector(document)
                     .OfClass(typeof(ViewSchedule))
                     .Cast<ViewSchedule>()
                     .Where(schedule => !schedule.IsTemplate))
        {
            foreach (Parameter parameter in schedule.Parameters)
            {
                string? name = parameter.Definition?.Name;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name!.Trim());
                }
            }
        }

        return names.ToArray();
    }

    private static bool ContainsExcludedValue(Parameter parameter, string excludedValue)
    {
        string value = ReadParameterValue(parameter);
        return value.IndexOf(excludedValue, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private static string ReadParameterValue(Parameter parameter)
    {
        try
        {
            if (parameter.StorageType == StorageType.String)
            {
                return parameter.AsString() ?? string.Empty;
            }

            return parameter.AsValueString()
                   ?? parameter.StorageType switch
                   {
                       StorageType.Integer => parameter.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture),
                       StorageType.Double => parameter.AsDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                       StorageType.ElementId => RevitElementIds.GetValue(parameter.AsElementId()).ToString(System.Globalization.CultureInfo.InvariantCulture),
                       _ => string.Empty
                   };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadHeaderTitle(ViewSchedule schedule)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        header.RefreshData();
        if (header.NumberOfRows == 0)
        {
            return string.Empty;
        }

        int firstRow = header.FirstRowNumber;
        for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
        {
            string value = schedule.GetCellText(SectionType.Header, firstRow, column) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static int GetSegmentIndex(ScheduleSheetInstance instance)
    {
#if REVIT2022_OR_GREATER
        return instance.SegmentIndex;
#else
        return 0;
#endif
    }
}
