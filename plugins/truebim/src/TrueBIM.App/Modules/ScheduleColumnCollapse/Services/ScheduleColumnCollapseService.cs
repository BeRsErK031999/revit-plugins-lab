using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Models;
using TrueBIM.App.Modules.ScheduleColumnCollapse.UI;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.ScheduleColumnCollapse.Services;

public sealed class ScheduleColumnCollapseService
{
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

        ViewSchedule? targetSchedule = ResolveTargetSchedule(uiDocument, ownerWindowHandle, out string? resolveError);
        if (targetSchedule is null)
        {
            return ScheduleColumnCollapseResult.Failure(resolveError ?? "Не удалось определить спецификацию для сворачивания.");
        }

        return Collapse(targetSchedule);
    }

    public ScheduleColumnCollapseResult Collapse(ViewSchedule targetSchedule)
    {
        Guard.NotNull(targetSchedule, nameof(targetSchedule));

        Document document = targetSchedule.Document;
        using Transaction transaction = new(document, "TrueBIM: свернуть ВРС");
        transaction.Start();

        try
        {
            ScheduleDefinition definition = targetSchedule.Definition;
            IReadOnlyList<ScheduleFieldId> fieldIds = definition.GetFieldOrder().ToList();

            IReadOnlyList<FieldSnapshot> snapshots = CreateFieldSnapshots(targetSchedule, fieldIds);
            IReadOnlyList<ScheduleColumnVisibilityDecision> decisions = analyzer.Analyze(snapshots.Select(snapshot => snapshot.Column));
            // Rolling back the inspection can invalidate the previous definition wrapper.
            definition = targetSchedule.Definition;

            int hiddenColumnCount = 0;
            int visibleColumnCount = 0;
            int unchangedColumnCount = 0;

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

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                return ScheduleColumnCollapseResult.Failure("Revit отменил изменение видимости столбцов спецификации.");
            }
            logger.Info(
                $"Collapsed schedule '{targetSchedule.Name}' in place. Hidden fields: {hiddenColumnCount}; visible fields: {visibleColumnCount}; unchanged fields: {unchangedColumnCount}.");

            return new ScheduleColumnCollapseResult(
                Succeeded: true,
                Message: "Спецификация обновлена: ненулевые числовые столбцы показаны, нулевые и пустые скрыты.",
                ScheduleId: targetSchedule.Id,
                ScheduleName: targetSchedule.Name,
                HiddenColumnCount: hiddenColumnCount,
                VisibleColumnCount: visibleColumnCount,
                UnchangedColumnCount: unchangedColumnCount);
        }
        catch (Exception exception)
        {
            logger.Error("Failed to collapse schedule columns.", exception);
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }
            throw;
        }
    }

    private static ViewSchedule? ResolveTargetSchedule(UIDocument uiDocument, IntPtr ownerWindowHandle, out string? error)
    {
        Document document = uiDocument.Document;
        ViewSchedule? activeSchedule = GetActiveSchedule(uiDocument);
        IReadOnlyList<ViewSchedule> selectedProjectBrowserSchedules = CollectSelectedProjectBrowserSchedules(uiDocument);

        ScheduleSourceSelectionWindow sourceSelectionWindow = new(
            activeSchedule?.Name,
            selectedProjectBrowserSchedules.Select(schedule => schedule.Name).ToList(),
            ownerWindowHandle);

        bool? sourceDialogResult = sourceSelectionWindow.ShowDialog();
        if (sourceDialogResult != true)
        {
            error = "Выбор спецификации отменён.";
            return null;
        }

        switch (sourceSelectionWindow.SelectedSource)
        {
            case ScheduleSourceSelection.ActiveView:
                if (activeSchedule is null)
                {
                    error = "Активное окно сейчас не является спецификацией.";
                    return null;
                }

                error = null;
                return activeSchedule;
            case ScheduleSourceSelection.ProjectBrowserSelection:
                if (selectedProjectBrowserSchedules.Count == 0)
                {
                    error = "В диспетчере проекта не выбрана спецификация.";
                    return null;
                }

                if (selectedProjectBrowserSchedules.Count > 1)
                {
                    error = "В диспетчере проекта выбрано несколько спецификаций. Оставьте выбранной одну спецификацию.";
                    return null;
                }

                error = null;
                return selectedProjectBrowserSchedules[0];
            case ScheduleSourceSelection.ListSelection:
                return ResolveTargetScheduleFromList(uiDocument, ownerWindowHandle, out error);
            default:
                error = "Не удалось определить способ выбора спецификации.";
                return null;
        }
    }

    private static ViewSchedule? ResolveTargetScheduleFromList(
        UIDocument uiDocument,
        IntPtr ownerWindowHandle,
        out string? error)
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

    private static ViewSchedule? GetActiveSchedule(UIDocument uiDocument)
    {
        return uiDocument.ActiveView is ViewSchedule activeSchedule && !activeSchedule.IsTemplate
            ? activeSchedule
            : ScheduleActiveViewTracker.GetLastActivatedSchedule(uiDocument);
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

        if (GetActiveSchedule(uiDocument) is ViewSchedule activeSchedule)
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

    private static IReadOnlyList<ViewSchedule> CollectSelectedProjectBrowserSchedules(UIDocument uiDocument)
    {
        Document document = uiDocument.Document;
        return DistinctSchedulesById(uiDocument.Selection
            .GetElementIds()
            .Select(document.GetElement)
            .OfType<ViewSchedule>()
            .Where(schedule => !schedule.IsTemplate)
            .ToList());
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
        Document document = schedule.Document;
        ScheduleDefinition definition = schedule.Definition;
        Dictionary<ScheduleFieldId, bool> originalVisibility = fieldIds.ToDictionary(
            fieldId => fieldId,
            fieldId => definition.GetField(fieldId).IsHidden);
        HashSet<ScheduleFieldId> serviceFieldIds = new(
            definition.GetFilters().Select(filter => filter.FieldId)
                .Concat(definition.GetSortGroupFields().Select(sort => sort.FieldId)));

        // Reading hidden numeric fields requires temporarily exposing their body columns.
        // Roll this back before applying decisions: Keep must preserve the original state.
        using SubTransaction readingTransaction = new(document);
        readingTransaction.Start();
        foreach (ScheduleFieldId fieldId in fieldIds)
        {
            ScheduleField field = definition.GetField(fieldId);
            if (field.IsHidden && IsNumericField(field) && !serviceFieldIds.Contains(fieldId))
            {
                TrySetHidden(field, isHidden: false);
            }
        }

        document.Regenerate();
        using TableData table = schedule.GetTableData();
        using TableSectionData body = table.GetSectionData(SectionType.Body);
        if (!body.RefreshData())
        {
            throw new InvalidOperationException("Не удалось обновить данные спецификации для анализа столбцов.");
        }

        int visibleFieldCount = fieldIds.Count(fieldId => !definition.GetField(fieldId).IsHidden);
        if (body.NumberOfColumns != visibleFieldCount)
        {
            throw new InvalidOperationException(
                $"Не удалось сопоставить поля спецификации с колонками таблицы: полей {visibleFieldCount}, колонок {body.NumberOfColumns}.");
        }

        List<FieldSnapshot> snapshots = new();
        using Units units = document.GetUnits();
        int columnNumber = body.FirstColumnNumber;
        foreach (ScheduleFieldId fieldId in fieldIds)
        {
            ScheduleField field = definition.GetField(fieldId);
            bool isNumeric = IsNumericField(field);
            List<string> cellTexts = new();
            if (!field.IsHidden)
            {
                if (isNumeric && body.NumberOfRows > 0)
                {
                    for (int rowNumber = body.FirstRowNumber; rowNumber <= body.LastRowNumber; rowNumber++)
                    {
                        cellTexts.Add(schedule.GetCellText(SectionType.Body, rowNumber, columnNumber));
                    }
                }

                columnNumber++;
            }

            snapshots.Add(new FieldSnapshot(
                fieldId,
                new ScheduleColumnState(
                    FieldName: field.GetName(),
                    ColumnHeading: field.ColumnHeading,
                    IsHidden: originalVisibility[fieldId],
                    CanHide: !field.IsHidden && !serviceFieldIds.Contains(fieldId),
                    CellTexts: cellTexts,
                    IsNumeric: isNumeric,
                    ParsedNumericValues: ParseNumericValues(units, field, cellTexts))));
        }

        readingTransaction.RollBack();
        return snapshots;
    }

    private static bool IsNumericField(ScheduleField field)
    {
#if REVIT2022_OR_GREATER
        ForgeTypeId specTypeId = field.GetSpecTypeId();
        return UnitUtils.IsMeasurableSpec(specTypeId)
            || specTypeId == SpecTypeId.Int.Integer
            || field.FieldType == ScheduleFieldType.Count;
#else
#pragma warning disable CS0618 // Revit 2019-2021 expose schedule units through UnitType.
        return field.UnitType != UnitType.UT_Undefined || field.FieldType == ScheduleFieldType.Count;
#pragma warning restore CS0618
#endif
    }

    private static IReadOnlyList<double?>? ParseNumericValues(
        Units units,
        ScheduleField field,
        IReadOnlyList<string> cellTexts)
    {
#if REVIT2022_OR_GREATER
        ForgeTypeId specTypeId = field.GetSpecTypeId();
        if (!UnitUtils.IsMeasurableSpec(specTypeId))
        {
            return null;
        }
#else
#pragma warning disable CS0618 // Revit 2019-2021 expose schedule units through UnitType.
        UnitType unitType = field.UnitType;
        if (unitType == UnitType.UT_Undefined)
        {
            return null;
        }
#endif

        using ValueParsingOptions options = new();
        using FormatOptions format = field.GetFormatOptions();
        options.SetFormatOptions(format);
        List<double?> values = new();
        foreach (string cellText in cellTexts)
        {
            string normalized = cellText.Replace('\u00a0', ' ').Replace('\u202f', ' ').Replace('\u2212', '-');
#if REVIT2022_OR_GREATER
            bool parsed = UnitFormatUtils.TryParse(units, specTypeId, normalized, options, out double value);
#else
            bool parsed = UnitFormatUtils.TryParse(units, unitType, normalized, options, out double value);
#pragma warning restore CS0618
#endif
            values.Add(parsed ? value : null);
        }

        return values;
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
