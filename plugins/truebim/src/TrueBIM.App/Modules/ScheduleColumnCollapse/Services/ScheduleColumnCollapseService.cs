using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
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

    public ScheduleColumnCollapseResult Collapse(UIDocument uiDocument)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));

        Document document = uiDocument.Document;
        ViewSchedule? sourceSchedule = ResolveTargetSchedule(uiDocument, out string? resolveError);
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

    private static ViewSchedule? ResolveTargetSchedule(UIDocument uiDocument, out string? error)
    {
        Document document = uiDocument.Document;
        IReadOnlyList<ViewSchedule> selectedSchedules = uiDocument.Selection
            .GetElementIds()
            .Select(document.GetElement)
            .OfType<ScheduleSheetInstance>()
            .Select(instance => document.GetElement(instance.ScheduleId) as ViewSchedule)
            .Where(schedule => schedule is not null)
            .Cast<ViewSchedule>()
            .ToList();
        selectedSchedules = DistinctSchedulesById(selectedSchedules);

        if (selectedSchedules.Count == 1)
        {
            error = null;
            return selectedSchedules[0];
        }

        if (selectedSchedules.Count > 1)
        {
            error = "Выберите на листе только одну спецификацию и запустите инструмент ещё раз.";
            return null;
        }

        if (document.ActiveView is ViewSchedule activeSchedule)
        {
            error = null;
            return activeSchedule;
        }

        if (document.ActiveView is ViewSheet activeSheet)
        {
            IReadOnlyList<ViewSchedule> sheetSchedules = new FilteredElementCollector(document, activeSheet.Id)
                .OfClass(typeof(ScheduleSheetInstance))
                .Cast<ScheduleSheetInstance>()
                .Select(instance => document.GetElement(instance.ScheduleId) as ViewSchedule)
                .Where(schedule => schedule is not null)
                .Cast<ViewSchedule>()
                .ToList();
            sheetSchedules = DistinctSchedulesById(sheetSchedules);

            if (sheetSchedules.Count == 1)
            {
                error = null;
                return sheetSchedules[0];
            }

            error = sheetSchedules.Count == 0
                ? "На активном листе не найдено размещённых спецификаций. Откройте спецификацию или выберите её на листе."
                : "На активном листе найдено несколько спецификаций. Выберите нужную спецификацию на листе и запустите инструмент ещё раз.";
            return null;
        }

        error = "Откройте спецификацию или лист со спецификацией, затем запустите инструмент.";
        return null;
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
