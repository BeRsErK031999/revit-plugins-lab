using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishRoomScheduleBuilder
{
    private const double TitleRowHeightMillimeters = 10;
    private const double ColumnHeaderRowHeightMillimeters = 8;

    private readonly FinishScheduleMetadataService metadataService;

    public FinishRoomScheduleBuilder(FinishScheduleMetadataService metadataService)
    {
        this.metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
    }

    public FinishRoomSchedulePreflight Preflight(Document document, FinishRoomSchedulePlan plan)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        List<ViewSchedule> schedules = CollectSchedules(document);
        List<ViewSchedule> exactName = schedules
            .Where(schedule => string.Equals(schedule.Name, plan.ScheduleName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exactName.Any(schedule => !metadataService.IsManaged(schedule)))
        {
            return Conflict(
                plan,
                $"Спецификация «{plan.ScheduleName}» уже существует и не принадлежит TrueBIM. "
                    + "Переименуйте её или задайте другое имя для ведомости отделки.");
        }

        List<ViewSchedule> managed = schedules
            .Where(IsRoomSchedule)
            .Where(metadataService.IsManaged)
            .ToList();
        if (managed.Count > 1)
        {
            return Conflict(
                plan,
                "В проекте найдено несколько ведомостей отделки с маркером TrueBIM. "
                    + "Оставьте одну управляемую ведомость и повторите запуск.");
        }

        if (managed.Count == 0)
        {
            return new FinishRoomSchedulePreflight(plan, FinishRoomScheduleAction.Create, null, []);
        }

        ViewSchedule existing = managed[0];
        FinishScheduleMetadata metadata = metadataService.Read(existing)!;
        bool unchanged = string.Equals(existing.Name, plan.ScheduleName, StringComparison.Ordinal)
            && string.Equals(metadata.SettingsHash, plan.SettingsHash, StringComparison.Ordinal);
        return new FinishRoomSchedulePreflight(
            plan,
            unchanged ? FinishRoomScheduleAction.NoChanges : FinishRoomScheduleAction.Update,
            RevitElementIds.GetValue(existing.Id),
            []);
    }

    public FinishRoomScheduleApplyResult Apply(
        Document document,
        FinishRoomSchedulePreflight preflight)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (preflight is null)
        {
            throw new ArgumentNullException(nameof(preflight));
        }

        FinishRoomSchedulePlan plan = preflight.Plan
            ?? throw new InvalidOperationException("План спецификации отсутствует.");
        if (!preflight.RequiresTransaction)
        {
            if (!preflight.ScheduleId.HasValue)
            {
                throw new InvalidOperationException("Не найден ElementId актуальной спецификации.");
            }

            return new FinishRoomScheduleApplyResult(
                preflight.ScheduleId.Value,
                plan.ScheduleName,
                preflight.Action);
        }

        FinishRoomSchedulePreflight current = Preflight(document, plan);
        if (current.Action != preflight.Action || current.ScheduleId != preflight.ScheduleId)
        {
            throw new InvalidOperationException(
                "Состав спецификаций изменился после preflight. Повторите формирование ведомости отделки.");
        }

        using Transaction transaction = new(document, "TrueBIM: создать ведомость отделки");
        FinishTransactionStatus.EnsureStarted(transaction);
        try
        {
            ViewSchedule schedule = preflight.Action == FinishRoomScheduleAction.Create
                ? CreateSchedule(document)
                : GetManagedSchedule(document, preflight.ScheduleId!.Value);
            schedule.Name = plan.ScheduleName;
            ConfigureSchedule(document, schedule, plan);
            metadataService.Write(schedule, plan);
            FinishTransactionStatus.EnsureCommitted(transaction);
            return new FinishRoomScheduleApplyResult(
                RevitElementIds.GetValue(schedule.Id),
                schedule.Name,
                preflight.Action);
        }
        catch
        {
            FinishTransactionStatus.RollBackIfStarted(transaction);
            throw;
        }
    }

    private static ViewSchedule CreateSchedule(Document document)
    {
        ElementId categoryId = RevitElementIds.Create((long)BuiltInCategory.OST_Rooms);
        if (!ViewSchedule.IsValidCategoryForSchedule(categoryId))
        {
            throw new InvalidOperationException("Категория помещений недоступна для спецификации Revit.");
        }

        return ViewSchedule.CreateSchedule(document, categoryId);
    }

    private ViewSchedule GetManagedSchedule(Document document, long scheduleId)
    {
        ViewSchedule schedule = document.GetElement(RevitElementIds.Create(scheduleId)) as ViewSchedule
            ?? throw new InvalidOperationException("Управляемая ведомость отделки больше не существует.");
        if (!metadataService.IsManaged(schedule))
        {
            throw new InvalidOperationException("Ведомость отделки больше не содержит маркер владения TrueBIM.");
        }

        return schedule;
    }

    private static void ConfigureSchedule(
        Document document,
        ViewSchedule schedule,
        FinishRoomSchedulePlan plan)
    {
        ScheduleDefinition definition = schedule.Definition;
        ClearDefinition(definition);
        definition.ShowTitle = true;
        definition.ShowHeaders = true;
#if REVIT2022_OR_GREATER
        definition.ShowGridLines = true;
#endif
        definition.IsItemized = false;

        IList<SchedulableField> availableFields = definition.GetSchedulableFields();
        ElementId thinLineStyleId = GetLineStyleId(document, BuiltInCategory.OST_CurvesThinLines);
        ElementId mediumLineStyleId = GetLineStyleId(document, BuiltInCategory.OST_CurvesMediumLines);
        ScheduleField? sortField = null;
        foreach (FinishRoomScheduleColumn column in plan.Columns)
        {
            ScheduleField field = AddField(definition, availableFields, column.Parameter);
            field.ColumnHeading = column.Heading;
            field.SheetColumnWidth = FinishScheduleUnitAdapter.MillimetersToInternal(column.WidthMillimeters);
            ConfigureBodyField(field, column.Kind, thinLineStyleId);
            sortField ??= field;
        }

        if (sortField is null)
        {
            throw new InvalidOperationException("Не удалось добавить поле сортировки ведомости отделки.");
        }

        definition.AddSortGroupField(new ScheduleSortGroupField(sortField.FieldId));
        AddScopeFilter(definition, availableFields, plan.ScopeFilter);
        ConfigureHeader(schedule, mediumLineStyleId);
    }

    private static void ConfigureBodyField(
        ScheduleField field,
        FinishRoomScheduleColumnKind kind,
        ElementId lineStyleId)
    {
        field.HorizontalAlignment = kind == FinishRoomScheduleColumnKind.Area
            ? ScheduleHorizontalAlignment.Center
            : ScheduleHorizontalAlignment.Left;
        using TableCellStyle style = field.GetStyle();
        using TableCellStyleOverrideOptions overrides = style.GetCellStyleOverrideOptions();
        style.FontVerticalAlignment = VerticalAlignmentStyle.Middle;
        overrides.VerticalAlignment = true;
        ApplyBorders(style, overrides, lineStyleId);
        style.SetCellStyleOverrideOptions(overrides);
        field.SetStyle(style);
    }

    private static void ConfigureHeader(ViewSchedule schedule, ElementId lineStyleId)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            double height = row == header.FirstRowNumber
                ? TitleRowHeightMillimeters
                : ColumnHeaderRowHeightMillimeters;
            header.SetRowHeight(row, FinishScheduleUnitAdapter.MillimetersToInternal(height));
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                if (!header.AllowOverrideCellStyle(row, column))
                {
                    continue;
                }

                using TableCellStyle style = header.GetTableCellStyle(row, column);
                using TableCellStyleOverrideOptions overrides = style.GetCellStyleOverrideOptions();
                style.FontHorizontalAlignment = HorizontalAlignmentStyle.Center;
                style.FontVerticalAlignment = VerticalAlignmentStyle.Middle;
                overrides.HorizontalAlignment = true;
                overrides.VerticalAlignment = true;
                ApplyBorders(style, overrides, lineStyleId);
                style.SetCellStyleOverrideOptions(overrides);
                header.SetCellStyle(row, column, style);
            }
        }
    }

    private static void ApplyBorders(
        TableCellStyle style,
        TableCellStyleOverrideOptions overrides,
        ElementId lineStyleId)
    {
        if (lineStyleId == ElementId.InvalidElementId)
        {
            return;
        }

        style.BorderTopLineStyle = lineStyleId;
        style.BorderBottomLineStyle = lineStyleId;
        style.BorderLeftLineStyle = lineStyleId;
        style.BorderRightLineStyle = lineStyleId;
        overrides.BorderTopLineStyle = true;
        overrides.BorderBottomLineStyle = true;
        overrides.BorderLeftLineStyle = true;
        overrides.BorderRightLineStyle = true;
    }

    private static ElementId GetLineStyleId(Document document, BuiltInCategory builtInCategory)
    {
        Category? category = Category.GetCategory(document, builtInCategory);
        GraphicsStyle? style = category?.GetGraphicsStyle(GraphicsStyleType.Projection);
        return style?.Id ?? ElementId.InvalidElementId;
    }

    private static void ClearDefinition(ScheduleDefinition definition)
    {
        for (int index = definition.GetFilterCount() - 1; index >= 0; index--)
        {
            definition.RemoveFilter(index);
        }

        definition.ClearSortGroupFields();
        foreach (ScheduleFieldId fieldId in definition.GetFieldOrder().Reverse().ToArray())
        {
            definition.RemoveField(fieldId);
        }
    }

    private static ScheduleField AddField(
        ScheduleDefinition definition,
        IEnumerable<SchedulableField> availableFields,
        ParameterReference reference)
    {
        SchedulableField schedulable = availableFields.FirstOrDefault(field => Matches(field, reference))
            ?? throw new InvalidOperationException(
                $"Параметр «{reference.Name}» недоступен как поле спецификации помещений.");
        return definition.AddField(schedulable);
    }

    private static bool Matches(SchedulableField field, ParameterReference reference)
    {
        long expectedId = reference.IdentityKind == ParameterIdentityKind.BuiltIn
            ? reference.BuiltInParameterId!.Value
            : reference.DefinitionElementId!.Value;
        return RevitElementIds.GetValue(field.ParameterId) == expectedId;
    }

    private static void AddScopeFilter(
        ScheduleDefinition definition,
        IList<SchedulableField> availableFields,
        FinishRoomScheduleScopeFilter scope)
    {
        if (scope.Kind == ReportScopeKind.EntireProject)
        {
            return;
        }

        ParameterReference reference = scope.Kind == ReportScopeKind.Level
            ? ParameterReference.BuiltIn(
                "Уровень",
                (long)BuiltInParameter.ROOM_LEVEL_ID,
                ParameterBindingKind.Instance,
                ParameterStorageKind.ElementId)
            : scope.Parameter ?? throw new InvalidOperationException("Не выбран параметр раздела.");
        ScheduleField field = AddField(definition, availableFields, reference);
        field.IsHidden = true;
        if (!definition.CanFilter() || !definition.CanFilterByValue(field.FieldId))
        {
            throw new InvalidOperationException($"Поле «{reference.Name}» не поддерживает фильтрацию в Revit.");
        }

        using ScheduleFilter filter = CreateFilter(field, scope);
        definition.AddFilter(filter);
    }

    private static ScheduleFilter CreateFilter(
        ScheduleField field,
        FinishRoomScheduleScopeFilter scope)
    {
        const ScheduleFilterType filterType = ScheduleFilterType.Equal;
        return scope.StorageKind switch
        {
            ParameterStorageKind.String => new ScheduleFilter(field.FieldId, filterType, scope.RawValue),
            ParameterStorageKind.Integer => new ScheduleFilter(
                field.FieldId,
                filterType,
                ParseInteger(scope.RawValue)),
            ParameterStorageKind.Double => new ScheduleFilter(
                field.FieldId,
                filterType,
                ParseDouble(scope.RawValue)),
            ParameterStorageKind.ElementId => new ScheduleFilter(
                field.FieldId,
                filterType,
                RevitElementIds.Create(ParseLong(scope.RawValue))),
            _ => throw new InvalidOperationException("Тип параметра не поддерживает фильтрацию ведомости отделки.")
        };
    }

    private static int ParseInteger(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            return result;
        }

        throw new FormatException($"«{value}» не является целым значением фильтра.");
    }

    private static long ParseLong(string value)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
        {
            return result;
        }

        throw new FormatException($"«{value}» не является ElementId фильтра.");
    }

    private static double ParseDouble(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
        {
            return result;
        }

        throw new FormatException($"«{value}» не является числовым значением фильтра.");
    }

    private static FinishRoomSchedulePreflight Conflict(FinishRoomSchedulePlan plan, string message)
    {
        return new FinishRoomSchedulePreflight(
            plan,
            FinishRoomScheduleAction.Blocked,
            null,
            [
                new FinishWriteIssue(
                    FinishWriteIssueCode.ScheduleNameConflict,
                    FinishWriteIssueSeverity.Critical,
                    message)
            ]);
    }

    private static List<ViewSchedule> CollectSchedules(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .Where(schedule => !schedule.IsTemplate)
            .ToList();
    }

    private static bool IsRoomSchedule(ViewSchedule schedule)
    {
        return RevitElementIds.GetValue(schedule.Definition.CategoryId)
            == (long)BuiltInCategory.OST_Rooms;
    }
}

internal static class FinishScheduleUnitAdapter
{
    public static double MillimetersToInternal(double millimeters)
    {
#if REVIT2022_OR_GREATER
        return UnitUtils.ConvertToInternalUnits(millimeters, UnitTypeId.Millimeters);
#else
#pragma warning disable CS0618
        return UnitUtils.ConvertToInternalUnits(millimeters, DisplayUnitType.DUT_MILLIMETERS);
#pragma warning restore CS0618
#endif
    }
}
