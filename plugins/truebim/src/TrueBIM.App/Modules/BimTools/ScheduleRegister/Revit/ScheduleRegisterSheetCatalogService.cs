using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Models;
using TrueBIM.App.Modules.BimTools.ScheduleRegister.Services;
using TrueBIM.App.Modules.Print.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleRegister.Revit;

public sealed class ScheduleRegisterSheetCatalogService
{
    public IReadOnlyList<ScheduleRegisterSheetOption> Collect(
        Document document,
        IReadOnlyCollection<ElementId> selectedSheetIds)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(selectedSheetIds, nameof(selectedSheetIds));

        HashSet<long> selectedIds = selectedSheetIds
            .Where(id => document.GetElement(id) is ViewSheet)
            .Select(RevitElementIds.GetValue)
            .ToHashSet();

        Dictionary<long, HashSet<long>> scheduleIdsBySheet = [];
        foreach (ScheduleSheetInstance instance in new FilteredElementCollector(document)
                     .OfClass(typeof(ScheduleSheetInstance))
                     .Cast<ScheduleSheetInstance>())
        {
            if (instance.IsTitleblockRevisionSchedule
                || document.GetElement(instance.ScheduleId) is not ViewSchedule schedule
                || ScheduleRegisterNameService.IsTemplateOrGeneratedCopy(schedule.Name))
            {
                continue;
            }

            long sheetId = RevitElementIds.GetValue(instance.OwnerViewId);
            if (!scheduleIdsBySheet.TryGetValue(sheetId, out HashSet<long>? scheduleIds))
            {
                scheduleIds = [];
                scheduleIdsBySheet.Add(sheetId, scheduleIds);
            }

            scheduleIds.Add(RevitElementIds.GetValue(instance.ScheduleId));
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Where(sheet => !sheet.IsTemplate && !sheet.IsPlaceholder)
            .Where(sheet => selectedIds.Contains(RevitElementIds.GetValue(sheet.Id)))
            .OrderBy(sheet => sheet.SheetNumber, PrintSheetNumberComparer.Instance)
            .ThenBy(sheet => sheet.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(sheet =>
            {
                long sheetId = RevitElementIds.GetValue(sheet.Id);
                int scheduleCount = scheduleIdsBySheet.TryGetValue(sheetId, out HashSet<long>? ids)
                    ? ids.Count
                    : 0;
                return new ScheduleRegisterSheetOption(
                    sheetId,
                    sheet.SheetNumber ?? string.Empty,
                    sheet.Name ?? string.Empty,
                    scheduleCount);
            })
            .ToArray();
    }
}
