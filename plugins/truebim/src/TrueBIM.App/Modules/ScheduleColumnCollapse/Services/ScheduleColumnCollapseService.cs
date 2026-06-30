using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.UI;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Services;

public sealed class ScheduleColumnCollapseService
{
    private const string CollapsedNameSuffix = " - свернутая";
    private readonly ScheduleColumnVisibilityAnalyzer analyzer;
    private readonly ITrueBimLogger logger;

    public ScheduleColumnCollapseService(ScheduleColumnVisibilityAnalyzer analyzer, ITrueBimLogger logger)
    {
        this.analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ScheduleColumnCollapseResult Collapse(UIDocument uiDocument, IntPtr ownerWindowHandle = default)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));

        Document document = uiDocument.Document;
        ViewSchedule? sourceSchedule = ResolveTargetSchedule(uiDocument, ownerWindowHandle, out string? resolveError);
        if (sourceSchedule is null)
        {
            return ScheduleColumnCollapseResult.Failure(resolveError ?? "Не удалось определить спецификацию для сворачивания.");
        }

        using Transaction transaction = new(document, "TrueBIM: свернуть ВРС");
        transaction.Start();

        try
        {
            ElementId collapsedScheduleId = sourceSchedule.Duplicate(ViewDuplicateOption.Duplicate);
            ViewSchedule collapsedSchedule = (ViewSchedule)document.GetElement(collapsedScheduleId);
            collapsedSchedule.Name = CreateUniqueScheduleName(document, sourceSchedule.Name + CollapsedNameSuffix);

            ScheduleDefinition definition = collapsedSchedule.Definition;
            IReadOnlyList<ScheduleFieldId> fieldIds = definition.GetFieldOrder().ToList();
            IReadOnlyList<ScheduleFieldId> visibleFieldIds = fieldIds
                .Where(fieldId => !definition.GetField(fieldId).IsHidden)
                .ToList();

            document.Regenerate();

            IReadOnlyList<FieldSnapshot> snapshots = CreateFieldSnapshots(collapsedSchedule, visibleFieldIds);
            IReadOnlyList<ScheduleColumnVisibilityDecision> decisions = analyzer.Analyze(snapshots.Select(snapshot => snapshot.Column));

            int hiddenColumnCount = 0;
            int visibleColumnCount = 0;
            int unchangedColumnCount = fieldIds.Count - visibleFieldIds.Count;

            for (int index = 0; index < snapshots.Count; index++)
            {
                ScheduleField field = definition.GetField(snapshots[index].FieldId);
                ScheduleColumnVisibilityDecision decision = decisions[index];

                switch (decision.Action)
                {
                    case ScheduleColumnVisibilityAction.Hide:
                        if (TrySetHidden(field, isHidden: true))
                        {
                            hiddenColumnCount++;
                        }
                        else
                        {
                            unchangedColumnCount++;
                        }

                        break;
                    case ScheduleColumnVisibilityAction.Show:
                        if (TrySetHidden(field, isHidden: false))
                        {
                            visibleColumnCount++;
                        }
                        else
                        {
                            unchangedColumnCount++;
                        }

                        break;
                    case ScheduleColumnVisibilityAction.Keep:
                        unchangedColumnCount++;
                        break;
                    default:
                        unchangedColumnCount++;
                        break;
                }
            }

            transaction.Commit();
            logger.Info(
                $"Collapsed schedule '{sourceSchedule.Name}' into '{collapsedSchedule.Name}'. Hidden fields: {hiddenColumnCount}; visible fields: {visibleColumnCount}; unchanged fields: {unchangedColumnCount}.");

            return new ScheduleColumnCollapseResult(
                Succeeded: true,
                Message: "Копия спецификации создана, пустые числовые столбцы скрыты.",
                CollapsedScheduleId: collapsedSchedule.Id,
                SourceScheduleName: sourceSchedule.Name,
                CollapsedScheduleName: collapsedSchedule.Name,
                HiddenColumnCount: hiddenColumnCount,
                VisibleColumnCount: visibleColumnCount,
                UnchangedColumnCount: unchangedColumnCount);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collapse schedule columns.", exception);
            transaction.RollBack();
            throw;
        }
    }

    private static ViewSchedule? ResolveTargetSchedule(UIDocument uiDocument, IntPtr ownerWindowHandle, out string? error)
    {
        Document document = uiDocument.Document;
        IReadOnlyList<ScheduleSelectionItem> schedules = CollectScheduleSelectionItems(uiDocument);
        if (schedules.Count == 0)
        {
            error = "В документе не найдено спецификаций для сворачивания.";
            return null;
        }

        ScheduleSelectionWindow selectionWindow = new(
            schedules,
            "Выберите спецификацию, которую нужно свернуть.",
            ownerWindowHandle);

        bool? dialogResult = selectionWindow.ShowDialog();
        if (dialogResult != true || selectionWindow.SelectedSchedule is null)
        {
            error = "Выбор спецификации отменён.";
            return null;
        }

        error = null;
        return document.GetElement(selectionWindow.SelectedSchedule.ScheduleId) as ViewSchedule;
    }

    private static IReadOnlyList<ScheduleSelectionItem> CollectScheduleSelectionItems(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        List<ScheduleSelectionItem> items = new();
        HashSet<long> seenIds = new();

        void AddSchedule(ViewSchedule? schedule, string context)
        {
            if (schedule is null || schedule.IsTemplate)
            {
                return;
            }

            long scheduleId = RevitElementIds.GetValue(schedule.Id);
            if (!seenIds.Add(scheduleId))
            {
                return;
            }

            items.Add(new ScheduleSelectionItem(schedule.Id, schedule.Name, context));
        }

        foreach (ViewSchedule schedule in CollectSelectedSchedules(uiDocument))
        {
            AddSchedule(schedule, "Выбрано на листе");
        }

        if (document.ActiveView is ViewSchedule activeSchedule)
        {
            AddSchedule(activeSchedule, "Активная спецификация");
        }

        if (document.ActiveView is ViewSheet activeSheet)
        {
            string sheetContext = $"Активный лист {activeSheet.SheetNumber}: {activeSheet.Name}";
            foreach (ViewSchedule schedule in CollectSheetSchedules(document, activeSheet))
            {
                AddSchedule(schedule, sheetContext);
            }
        }

        foreach (ViewSchedule schedule in CollectDocumentSchedules(document))
        {
            AddSchedule(schedule, "Спецификация в документе");
        }

        return items;
    }

    private static IReadOnlyList<ViewSchedule> CollectSelectedSchedules(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        return DistinctSchedulesById(uiDocument.Selection
            .GetElementIds()
            .Select(document.GetElement)
            .OfType<ScheduleSheetInstance>()
            .Select(instance => document.GetElement(instance.ScheduleId) as ViewSchedule)
            .Where(schedule => schedule is not null && !schedule.IsTemplate)
            .Cast<ViewSchedule>()
            .ToList());
    }

    private static IReadOnlyList<ViewSchedule> CollectSheetSchedules(Document document, ViewSheet sheet)
    {
        return DistinctSchedulesById(new FilteredElementCollector(document, sheet.Id)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>()
            .Select(instance => document.GetElement(instance.ScheduleId) as ViewSchedule)
            .Where(schedule => schedule is not null && !schedule.IsTemplate)
            .Cast<ViewSchedule>()
            .ToList());
    }

    private static IReadOnlyList<ViewSchedule> CollectDocumentSchedules(Document document)
    {
        return DistinctSchedulesById(new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(schedule => !schedule.IsTemplate)
            .ToList())
            .OrderBy(schedule => schedule.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<FieldSnapshot> CreateFieldSnapshots(ViewSchedule schedule, IReadOnlyList<ScheduleFieldId> fieldIds)
    {
        TableSectionData body = schedule.GetTableData().GetSectionData(SectionType.Body);
        int firstColumn = body.FirstColumnNumber;
        int lastColumn = body.LastColumnNumber;
        int firstRow = body.FirstRowNumber;
        int lastRow = body.LastRowNumber;

        List<FieldSnapshot> snapshots = new();
        ScheduleDefinition definition = schedule.Definition;
        for (int index = 0; index < fieldIds.Count; index++)
        {
            int columnNumber = firstColumn + index;
            if (columnNumber > lastColumn)
            {
                break;
            }

            ScheduleField field = definition.GetField(fieldIds[index]);
            List<string> cellTexts = new();
            for (int rowNumber = firstRow; rowNumber <= lastRow; rowNumber++)
            {
                cellTexts.Add(schedule.GetCellText(SectionType.Body, rowNumber, columnNumber));
            }

            snapshots.Add(new FieldSnapshot(
                fieldIds[index],
                new ScheduleColumnState(
                    FieldName: field.GetName(),
                    ColumnHeading: field.ColumnHeading,
                    IsHidden: field.IsHidden,
                    CanHide: true,
                    CellTexts: cellTexts)));
        }

        return snapshots;
    }

    private static bool TrySetHidden(ScheduleField field, bool isHidden)
    {
        try
        {
            field.IsHidden = isHidden;
            return field.IsHidden == isHidden;
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string CreateUniqueScheduleName(Document document, string baseName)
    {
        HashSet<string> existingNames = new(
            new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Select(schedule => schedule.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (int index = 1; index < 1000; index++)
        {
            string candidate = $"{baseName} {index}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private static IReadOnlyList<ViewSchedule> DistinctSchedulesById(IEnumerable<ViewSchedule> schedules)
    {
        HashSet<long> seenIds = new();
        List<ViewSchedule> distinctSchedules = new();
        foreach (ViewSchedule schedule in schedules)
        {
            if (seenIds.Add(RevitElementIds.GetValue(schedule.Id)))
            {
                distinctSchedules.Add(schedule);
            }
        }

        return distinctSchedules;
    }

    private sealed record FieldSnapshot(ScheduleFieldId FieldId, ScheduleColumnState Column);
}
