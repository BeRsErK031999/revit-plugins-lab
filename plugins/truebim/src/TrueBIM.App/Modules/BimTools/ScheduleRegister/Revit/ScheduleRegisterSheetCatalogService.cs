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
        IReadOnlyCollection<ElementId> selectedElementIds,
        ElementId activeViewId)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(selectedElementIds, nameof(selectedElementIds));
        Guard.NotNull(activeViewId, nameof(activeViewId));

        HashSet<long> explicitlySelectedSheetIds = selectedElementIds
            .Where(id => document.GetElement(id) is ViewSheet)
            .Select(RevitElementIds.GetValue)
            .ToHashSet();
        long? activeSheetId = document.GetElement(activeViewId) is ViewSheet activeSheet
            ? RevitElementIds.GetValue(activeSheet.Id)
            : null;
        bool useActiveSheetFallback = explicitlySelectedSheetIds.Count == 0 && activeSheetId.HasValue;

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
            .OrderBy(sheet => sheet.SheetNumber, PrintSheetNumberComparer.Instance)
            .ThenBy(sheet => sheet.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(sheet =>
            {
                long sheetId = RevitElementIds.GetValue(sheet.Id);
                int scheduleCount = scheduleIdsBySheet.TryGetValue(sheetId, out HashSet<long>? ids)
                    ? ids.Count
                    : 0;
                bool isSelected = explicitlySelectedSheetIds.Contains(sheetId)
                                  || (useActiveSheetFallback && activeSheetId == sheetId);
                return new ScheduleRegisterSheetOption(
                    sheetId,
                    sheet.SheetNumber ?? string.Empty,
                    sheet.Name ?? string.Empty,
                    scheduleCount,
                    isSelected);
            })
            .ToArray();
    }
}
