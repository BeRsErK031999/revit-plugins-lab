using System.Globalization;
using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishRoomScheduleBuilder
{
    private const string ManagedTitleTextTypeName =
        "TrueBIM • Ведомость отделки • Название 3,5 мм";
    private const string ManagedHeaderTextTypeName =
        "TrueBIM • Ведомость отделки • Заголовок 2,5 мм";
    private const string ManagedBodyTextTypeName =
        "TrueBIM • Ведомость отделки • Тело 2,5 мм";

    private readonly FinishScheduleMetadataService metadataService;
    private readonly ITrueBimLogger logger;

    public FinishRoomScheduleBuilder(
        FinishScheduleMetadataService metadataService,
        ITrueBimLogger logger)
    {
        this.metadataService = metadataService ?? throw new ArgumentNullException(nameof(metadataService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        FinishRoomSchedulePreflight preflight,
        FinishScheduleHeaderMode headerMode = FinishScheduleHeaderMode.Custom)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (preflight is null)
        {
            throw new ArgumentNullException(nameof(preflight));
        }

        if (!Enum.IsDefined(typeof(FinishScheduleHeaderMode), headerMode))
        {
            throw new ArgumentOutOfRangeException(nameof(headerMode), headerMode, null);
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

        using TransactionGroup group = new(document, "TrueBIM: настроить ведомость отделки");
        bool groupStarted = false;
        try
        {
            FinishTransactionStatus.EnsureStarted(group);
            groupStarted = true;
            ViewSchedule schedule = ConfigureDefinitionTransaction(
                document,
                preflight,
                plan,
                headerMode);
            long scheduleId = RevitElementIds.GetValue(schedule.Id);
            logger.Info(
                $"Finish Schedule definition committed. ScheduleId={scheduleId}; "
                    + $"Action={preflight.Action}; Fields={plan.Columns.Count}; HeaderMode={headerMode}.");
            ConfigureTableTransaction(document, scheduleId, plan, headerMode);
            FinishTransactionStatus.EnsureAssimilated(group);
            groupStarted = false;
            return new FinishRoomScheduleApplyResult(
                scheduleId,
                plan.ScheduleName,
                preflight.Action);
        }
        catch
        {
            if (groupStarted)
            {
                FinishTransactionStatus.RollBackIfStarted(group);
            }

            throw;
        }
    }

    private ViewSchedule ConfigureDefinitionTransaction(
        Document document,
        FinishRoomSchedulePreflight preflight,
        FinishRoomSchedulePlan plan,
        FinishScheduleHeaderMode headerMode)
    {
        using Transaction transaction = new(document, "TrueBIM: подготовить ведомость отделки");
        FinishTransactionStatus.EnsureStarted(transaction);
        try
        {
            ViewSchedule schedule = preflight.Action == FinishRoomScheduleAction.Create
                ? CreateSchedule(document)
                : GetManagedSchedule(document, preflight.ScheduleId!.Value);
            schedule.Name = plan.ScheduleName;
            ConfigureDefinition(document, schedule, plan, headerMode);
            document.Regenerate();
            FinishTransactionStatus.EnsureCommitted(transaction);
            return schedule;
        }
        catch
        {
            FinishTransactionStatus.RollBackIfStarted(transaction);
            throw;
        }
    }

    private void ConfigureTableTransaction(
        Document document,
        long scheduleId,
        FinishRoomSchedulePlan plan,
        FinishScheduleHeaderMode headerMode)
    {
        using Transaction transaction = new(document, "TrueBIM: оформить ведомость отделки");
        FinishTransactionStatus.EnsureStarted(transaction);
        try
        {
            ViewSchedule schedule = document.GetElement(RevitElementIds.Create(scheduleId)) as ViewSchedule
                ?? throw new InvalidOperationException(
                    "Созданная ведомость отделки недоступна для оформления.");
            ConfigureTable(document, schedule, plan, headerMode);
            metadataService.Write(schedule, plan);
            FinishTransactionStatus.EnsureCommitted(transaction);
            ViewSchedule committedSchedule = document.GetElement(
                    RevitElementIds.Create(scheduleId)) as ViewSchedule
                ?? throw new InvalidOperationException(
                    "Оформленная ведомость отделки недоступна после фиксации транзакции.");
            logger.Info(
                $"Finish Schedule text types verified after transaction commit. "
                    + $"ScheduleId={scheduleId}; "
                    + $"TitleTypeId={RevitElementIds.GetValue(committedSchedule.TitleTextTypeId)}; "
                    + $"HeaderTypeId={RevitElementIds.GetValue(committedSchedule.HeaderTextTypeId)}; "
                    + $"BodyTypeId={RevitElementIds.GetValue(committedSchedule.BodyTextTypeId)}.");
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

    private void ConfigureDefinition(
        Document document,
        ViewSchedule schedule,
        FinishRoomSchedulePlan plan,
        FinishScheduleHeaderMode headerMode)
    {
        ScheduleDefinition definition = schedule.Definition;
        ResetCustomHeader(schedule);
        ClearDefinition(definition);
        definition.ShowTitle = headerMode != FinishScheduleHeaderMode.None;
        definition.ShowHeaders = headerMode == FinishScheduleHeaderMode.Standard;
#if REVIT2022_OR_GREATER
        definition.ShowGridLines = true;
#endif
        definition.IsItemized = false;

        IList<SchedulableField> availableFields = definition.GetSchedulableFields();
        ElementId normalLineStyleId = GetLineStyleCategoryId(
            document,
            FinishRoomScheduleStyleRules.NormalLineStyleName,
            BuiltInCategory.OST_CurvesMediumLines);
        ElementId thinLineStyleId = GetLineStyleCategoryId(
            document,
            FinishRoomScheduleStyleRules.ThinLineStyleName,
            BuiltInCategory.OST_CurvesThinLines);
        EnsureLineStyles(document, normalLineStyleId, thinLineStyleId);
        ScheduleField? sortField = null;
        foreach (FinishRoomScheduleColumn column in plan.Columns)
        {
            ScheduleField field = AddField(definition, availableFields, column.Parameter);
            field.ColumnHeading = column.Heading;
            field.SheetColumnWidth = FinishScheduleUnitAdapter.MillimetersToInternal(column.WidthMillimeters);
            ConfigureBodyField(
                field,
                column.Kind,
                normalLineStyleId,
                thinLineStyleId);
            sortField ??= field;
        }

        if (sortField is null)
        {
            throw new InvalidOperationException("Не удалось добавить поле сортировки ведомости отделки.");
        }

        ScheduleSortGroupField sortGroupField = new(sortField.FieldId)
        {
            ShowBlankLine = FinishRoomScheduleStyleRules.ShowBlankLineBetweenGroups,
            ShowHeader = false,
            ShowFooter = false
        };
        definition.AddSortGroupField(sortGroupField);
        AddScopeFilter(definition, availableFields, plan.ScopeFilter);
    }

    private void ConfigureScheduleTextTypes(Document document, ViewSchedule schedule)
    {
        ElementId titleTextTypeId = EnsureManagedTextType(
            document,
            schedule.TitleTextTypeId,
            ManagedTitleTextTypeName,
            "названия спецификации",
            FinishRoomScheduleStyleRules.TitleTextSizeMillimeters,
            forceBold: true);
        ElementId headerTextTypeId = EnsureManagedTextType(
            document,
            schedule.HeaderTextTypeId,
            ManagedHeaderTextTypeName,
            "заголовков спецификации",
            FinishRoomScheduleStyleRules.ColumnHeaderTextSizeMillimeters,
            forceBold: false);
        ElementId bodyTextTypeId = EnsureManagedTextType(
            document,
            schedule.BodyTextTypeId,
            ManagedBodyTextTypeName,
            "тела спецификации",
            FinishRoomScheduleStyleRules.BodyTextSizeMillimeters,
            forceBold: false);

        schedule.TitleTextTypeId = titleTextTypeId;
        schedule.HeaderTextTypeId = headerTextTypeId;
        schedule.BodyTextTypeId = bodyTextTypeId;
        logger.Info(
            $"Finish Schedule text types configured. "
                + $"ScheduleId={RevitElementIds.GetValue(schedule.Id)}; "
                + $"TitleTypeId={RevitElementIds.GetValue(titleTextTypeId)}; "
                + $"HeaderTypeId={RevitElementIds.GetValue(headerTextTypeId)}; "
                + $"BodyTypeId={RevitElementIds.GetValue(bodyTextTypeId)}; "
                + $"TitleSizeMm={FinishRoomScheduleStyleRules.TitleTextSizeMillimeters}; "
                + $"HeaderSizeMm={FinishRoomScheduleStyleRules.ColumnHeaderTextSizeMillimeters}; "
                + $"BodySizeMm={FinishRoomScheduleStyleRules.BodyTextSizeMillimeters}.");
    }

    private static ElementId EnsureManagedTextType(
        Document document,
        ElementId sourceTypeId,
        string managedTypeName,
        string role,
        double textSizeMillimeters,
        bool forceBold)
    {
        TextNoteType sourceType = document.GetElement(sourceTypeId) as TextNoteType
            ?? throw new InvalidOperationException($"Не найден тип текста для {role}.");
        TextNoteType? managedType = new FilteredElementCollector(document)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .FirstOrDefault(type => string.Equals(
                type.Name,
                managedTypeName,
                StringComparison.Ordinal));
        managedType ??= (TextNoteType)sourceType.Duplicate(managedTypeName);

        string fontName = GetRequiredTextTypeParameter(
            sourceType,
            role,
            BuiltInParameter.TEXT_FONT,
            BuiltInParameter.TEXT_STYLE_FONT).AsString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fontName))
        {
            throw new InvalidOperationException(
                $"Тип текста для {role} содержит недопустимый шрифт.");
        }

        GetRequiredTextTypeParameter(
            managedType,
            role,
            BuiltInParameter.TEXT_FONT,
            BuiltInParameter.TEXT_STYLE_FONT).Set(fontName);
        GetRequiredTextTypeParameter(
            managedType,
            role,
            BuiltInParameter.TEXT_SIZE).Set(
                FinishScheduleUnitAdapter.MillimetersToInternal(textSizeMillimeters));
        SetTextStyleFlag(
            managedType,
            BuiltInParameter.TEXT_STYLE_BOLD,
            forceBold || ReadTextStyleFlag(sourceType, BuiltInParameter.TEXT_STYLE_BOLD));
        SetTextStyleFlag(
            managedType,
            BuiltInParameter.TEXT_STYLE_ITALIC,
            ReadTextStyleFlag(sourceType, BuiltInParameter.TEXT_STYLE_ITALIC));
        SetTextStyleFlag(
            managedType,
            BuiltInParameter.TEXT_STYLE_UNDERLINE,
            ReadTextStyleFlag(sourceType, BuiltInParameter.TEXT_STYLE_UNDERLINE));
        return managedType.Id;
    }

    private void ConfigureTable(
        Document document,
        ViewSchedule schedule,
        FinishRoomSchedulePlan plan,
        FinishScheduleHeaderMode headerMode)
    {
        bool scheduleRefreshed = schedule.RefreshData();
        if (!scheduleRefreshed)
        {
            throw new InvalidOperationException(
                "Revit не обновил табличные данные ведомости после фиксации полей.");
        }

        ConfigureScheduleTextTypes(document, schedule);

        ElementId normalLineStyleId = GetLineStyleCategoryId(
            document,
            FinishRoomScheduleStyleRules.NormalLineStyleName,
            BuiltInCategory.OST_CurvesMediumLines);
        ElementId thinLineStyleId = GetLineStyleCategoryId(
            document,
            FinishRoomScheduleStyleRules.ThinLineStyleName,
            BuiltInCategory.OST_CurvesThinLines);
        EnsureLineStyles(document, normalLineStyleId, thinLineStyleId);
        logger.Info(
            $"Finish Schedule line-style categories resolved. "
                + $"ScheduleId={RevitElementIds.GetValue(schedule.Id)}; "
                + $"NormalCategoryId={RevitElementIds.GetValue(normalLineStyleId)}; "
                + $"ThinCategoryId={RevitElementIds.GetValue(thinLineStyleId)}.");
        if (headerMode == FinishScheduleHeaderMode.Custom)
        {
            try
            {
                ConfigureHeader(
                    document,
                    schedule,
                    plan.Columns,
                    normalLineStyleId,
                    thinLineStyleId);
            }
            catch (Exception exception)
            {
                throw new FinishScheduleHeaderFormattingException(
                    "Revit не смог создать составную шапку ведомости отделки.",
                    exception);
            }
        }
        else
        {
            logger.Info(
                $"Finish Schedule custom header skipped. "
                    + $"ScheduleId={RevitElementIds.GetValue(schedule.Id)}; HeaderMode={headerMode}.");
        }

        ConfigureBody(schedule, normalLineStyleId, thinLineStyleId);
        logger.Info(
            $"Finish Schedule text types retained after table formatting. "
                + $"ScheduleId={RevitElementIds.GetValue(schedule.Id)}; "
                + $"TitleTypeId={RevitElementIds.GetValue(schedule.TitleTextTypeId)}; "
                + $"HeaderTypeId={RevitElementIds.GetValue(schedule.HeaderTextTypeId)}; "
                + $"BodyTypeId={RevitElementIds.GetValue(schedule.BodyTextTypeId)}.");
    }

    private static void ConfigureBodyField(
        ScheduleField field,
        FinishRoomScheduleColumnKind kind,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        field.HorizontalAlignment = kind == FinishRoomScheduleColumnKind.Area
            ? ScheduleHorizontalAlignment.Center
            : ScheduleHorizontalAlignment.Left;
        using TableCellStyle style = field.GetStyle();
        using TableCellStyleOverrideOptions overrides = style.GetCellStyleOverrideOptions();
        style.FontVerticalAlignment = VerticalAlignmentStyle.Middle;
        overrides.VerticalAlignment = true;
        UseScheduleTextDefaults(overrides);
        style.TextSize = FinishScheduleUnitAdapter.MillimetersToTableTextSize(
            FinishRoomScheduleStyleRules.BodyTextSizeMillimeters);
        overrides.FontSize = true;
        ApplyBorders(
            style,
            overrides,
            FinishRoomScheduleStyleRules.BodyBorders(isFirstRow: false, isLastRow: false),
            normalLineStyleId,
            thinLineStyleId);
        style.SetCellStyleOverrideOptions(overrides);
        field.SetStyle(style);
    }

    private void ConfigureHeader(
        Document document,
        ViewSchedule schedule,
        IReadOnlyList<FinishRoomScheduleColumn> columns,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        FinishScheduleTextStyle titleTextStyle = ReadScheduleTextStyle(
            document,
            schedule.TitleTextTypeId,
            "названия спецификации",
            FinishRoomScheduleStyleRules.TitleTextSizeMillimeters,
            forceBold: true);
        FinishScheduleTextStyle headerTextStyle = ReadScheduleTextStyle(
            document,
            schedule.HeaderTextTypeId,
            "заголовков спецификации",
            FinishRoomScheduleStyleRules.ColumnHeaderTextSizeMillimeters,
            forceBold: false);
        IReadOnlyList<FinishScheduleHeaderCell> cells =
            FinishRoomScheduleStyleRules.BuildHeaderCells(columns);
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        bool headerRefreshed = header.RefreshData();
        int initialRowCount = header.NumberOfRows;
        int initialColumnCount = header.NumberOfColumns;
        logger.Info(
            $"Finish Schedule title grid inspected. ScheduleId={RevitElementIds.GetValue(schedule.Id)}; "
                + $"Refreshed={headerRefreshed}; Rows={initialRowCount}; "
                + $"Columns={initialColumnCount}; ExpectedColumns={columns.Count}; "
                + $"RowRange={header.FirstRowNumber}..{header.LastRowNumber}; "
                + $"ColumnRange={header.FirstColumnNumber}..{header.LastColumnNumber}.");
        if (!headerRefreshed)
        {
            throw new InvalidOperationException(
                "Revit не обновил область заголовка ведомости перед оформлением.");
        }

        FinishScheduleHeaderNormalizationPlan normalization;
        try
        {
            normalization = FinishRoomScheduleStyleRules.BuildHeaderNormalizationPlan(
                initialRowCount,
                initialColumnCount,
                columns.Count);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Revit подготовил недопустимую область шапки: "
                    + $"строк {initialRowCount}, столбцов {initialColumnCount}; "
                    + $"ожидалось от 1 до {FinishRoomScheduleStyleRules.HeaderRowCount} строк "
                    + $"и от 1 до {columns.Count} столбцов до нормализации.",
                exception);
        }

        int titleRow = header.FirstRowNumber;
        int groupRow = titleRow + 1;
        for (int index = 0; index < normalization.ColumnsToInsert; index++)
        {
            int insertionColumn = header.LastColumnNumber;
            if (!header.CanInsertColumn(insertionColumn))
            {
                throw new InvalidOperationException(
                    $"Revit не разрешил добавить столбец {insertionColumn} в область заголовка.");
            }

            header.InsertColumn(insertionColumn);
        }

        for (int index = 0; index < normalization.RowsToInsert; index++)
        {
            int insertionRow = header.LastRowNumber + 1;
            if (!header.CanInsertRow(insertionRow))
            {
                throw new InvalidOperationException(
                    $"Revit не разрешил добавить строку {insertionRow} в область заголовка.");
            }

            header.InsertRow(insertionRow);
        }

        if (header.NumberOfRows != FinishRoomScheduleStyleRules.HeaderRowCount
            || header.NumberOfColumns != columns.Count)
        {
            throw new InvalidOperationException(
                $"Не удалось нормализовать шапку: было {initialRowCount}×{initialColumnCount}, "
                    + $"после вставки стало {header.NumberOfRows}×{header.NumberOfColumns}, "
                    + $"ожидалось {FinishRoomScheduleStyleRules.HeaderRowCount}×{columns.Count}.");
        }

        int firstColumn = header.FirstColumnNumber;
        int lastColumn = header.LastColumnNumber;
        for (int index = 0; index < columns.Count; index++)
        {
            header.SetColumnWidth(
                firstColumn + index,
                FinishScheduleUnitAdapter.MillimetersToInternal(columns[index].WidthMillimeters));
        }

        header.MergeCells(new TableMergedCell(titleRow, firstColumn, titleRow, lastColumn));
        header.SetCellText(titleRow, firstColumn, FinishRoomScheduleStyleRules.ScheduleTitleText);
        foreach (FinishScheduleHeaderCell cell in cells)
        {
            int top = groupRow + cell.TopRowOffset;
            int left = firstColumn + cell.LeftColumnIndex;
            int bottom = groupRow + cell.BottomRowOffset;
            int right = firstColumn + cell.RightColumnIndex;
            switch (cell.MergeMode)
            {
                case FinishScheduleHeaderMergeMode.CellMerge:
                    TableMergedCell mergedCell = new(top, left, bottom, right);
                    try
                    {
                        header.MergeCells(mergedCell);
                    }
                    catch (Autodesk.Revit.Exceptions.ArgumentException exception)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось объединить ячейки шапки "
                                + $"[{top},{left}]..[{bottom},{right}] внутри диапазона "
                                + $"строк {header.FirstRowNumber}..{header.LastRowNumber} и "
                                + $"столбцов {header.FirstColumnNumber}..{header.LastColumnNumber}.",
                            exception);
                    }

                    header.SetCellText(top, left, cell.Text);
                    break;
                case FinishScheduleHeaderMergeMode.None:
                    header.SetCellText(top, left, cell.Text);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Неизвестный режим объединения ячеек: {cell.MergeMode}.");
            }
        }

        int overridableCellCount = 0;
        int skippedCellCount = 0;
        int verifiedFontSizeCount = 0;
        int verifiedBorderCount = 0;
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            bool isTitleRow = row == titleRow;
            double height = row switch
            {
                _ when isTitleRow => FinishRoomScheduleStyleRules.TitleRowHeightMillimeters,
                _ when row == groupRow => FinishRoomScheduleStyleRules.GroupHeaderRowHeightMillimeters,
                _ => FinishRoomScheduleStyleRules.ColumnHeaderRowHeightMillimeters
            };
            header.SetRowHeight(row, FinishScheduleUnitAdapter.MillimetersToInternal(height));
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                if (!header.AllowOverrideCellStyle(row, column))
                {
                    skippedCellCount++;
                    continue;
                }

                overridableCellCount++;
                using TableCellStyle style = header.GetTableCellStyle(row, column);
                using TableCellStyleOverrideOptions overrides = style.GetCellStyleOverrideOptions();
                style.FontHorizontalAlignment = HorizontalAlignmentStyle.Center;
                style.FontVerticalAlignment = VerticalAlignmentStyle.Middle;
                overrides.HorizontalAlignment = true;
                overrides.VerticalAlignment = true;
                ApplyScheduleTextStyle(
                    style,
                    overrides,
                    isTitleRow ? titleTextStyle : headerTextStyle);
                ApplyBorders(
                    style,
                    overrides,
                    FinishRoomScheduleStyleRules.HeaderBorders,
                    normalLineStyleId,
                    thinLineStyleId);
                style.SetCellStyleOverrideOptions(overrides);
                header.SetCellStyle(row, column, style);
                using TableCellStyle appliedStyle = header.GetTableCellStyle(row, column);
                using TableCellStyleOverrideOptions appliedOverrides =
                    appliedStyle.GetCellStyleOverrideOptions();
                double expectedTextSize = isTitleRow
                    ? titleTextStyle.TextSize
                    : headerTextStyle.TextSize;
                if (appliedOverrides.FontSize
                    && Math.Abs(appliedStyle.TextSize - expectedTextSize) < 0.0000001)
                {
                    verifiedFontSizeCount++;
                }

                if (HasExpectedBorders(
                        appliedStyle,
                        appliedOverrides,
                        FinishRoomScheduleStyleRules.HeaderBorders,
                        normalLineStyleId,
                        thinLineStyleId))
                {
                    verifiedBorderCount++;
                }
            }
        }

        logger.Info(
            $"Finish Schedule custom header cell styles applied. "
                + $"ScheduleId={RevitElementIds.GetValue(schedule.Id)}; "
                + $"Overridable={overridableCellCount}; Skipped={skippedCellCount}; "
                + $"FontSizeVerified={verifiedFontSizeCount}; "
                + $"BordersVerified={verifiedBorderCount}.");
    }

    private static void ResetCustomHeader(ViewSchedule schedule)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        bool headerRefreshed = header.RefreshData();
        if (!headerRefreshed)
        {
            throw new InvalidOperationException(
                "Revit не обновил прежнюю шапку ведомости отделки перед очисткой.");
        }

        bool isManagedCustomLayout = schedule.Definition.ShowTitle
            && !schedule.Definition.ShowHeaders;
        if (!isManagedCustomLayout && !ContainsManagedCustomHeaderMarker(header))
        {
            return;
        }

        if (header.NumberOfRows <= 1 && header.NumberOfColumns <= 1)
        {
            return;
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<TableMergedCell> mergedCells = [];
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                TableMergedCell merged = header.GetMergedCell(row, column);
                if (merged.Top == merged.Bottom && merged.Left == merged.Right)
                {
                    continue;
                }

                string key = $"{merged.Top}:{merged.Left}:{merged.Bottom}:{merged.Right}";
                if (seen.Add(key))
                {
                    mergedCells.Add(merged);
                }
            }
        }

        foreach (TableMergedCell merged in mergedCells
                     .OrderByDescending(cell => cell.Bottom - cell.Top)
                     .ThenByDescending(cell => cell.Right - cell.Left))
        {
            if (!schedule.CanUngroupHeaders(
                    merged.Top,
                    merged.Left,
                    merged.Bottom,
                    merged.Right))
            {
                throw new InvalidOperationException(
                    "Не удалось разобрать прежнюю шапку ведомости отделки для обновления.");
            }

            schedule.UngroupHeaders(
                merged.Top,
                merged.Left,
                merged.Bottom,
                merged.Right);
        }

        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                if (header.AllowOverrideCellStyle(row, column))
                {
                    header.ResetCellOverride(row, column);
                }
            }
        }

        while (header.NumberOfRows > 1)
        {
            int row = header.LastRowNumber;
            if (!header.CanRemoveRow(row))
            {
                throw new InvalidOperationException(
                    "Не удалось удалить прежнюю строку шапки ведомости отделки.");
            }

            header.RemoveRow(row);
        }

        while (header.NumberOfColumns > 1)
        {
            int column = header.LastColumnNumber;
            if (!header.CanRemoveColumn(column))
            {
                throw new InvalidOperationException(
                    "Не удалось удалить прежний столбец шапки ведомости отделки.");
            }

            header.RemoveColumn(column);
        }
    }

    private static bool ContainsManagedCustomHeaderMarker(TableSectionData header)
    {
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                string text = header.GetCellText(row, column);
                if (string.Equals(
                        text,
                        FinishRoomScheduleStyleRules.FinishGroupHeaderText,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ConfigureBody(
        ViewSchedule schedule,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData body = table.GetSectionData(SectionType.Body);
        for (int row = body.FirstRowNumber; row <= body.LastRowNumber; row++)
        {
            FinishScheduleCellBorderRules borderRules = FinishRoomScheduleStyleRules.BodyBorders(
                row == body.FirstRowNumber,
                row == body.LastRowNumber);
            for (int column = body.FirstColumnNumber; column <= body.LastColumnNumber; column++)
            {
                if (!body.AllowOverrideCellStyle(row, column))
                {
                    continue;
                }

                using TableCellStyle style = body.GetTableCellStyle(row, column);
                using TableCellStyleOverrideOptions overrides = style.GetCellStyleOverrideOptions();
                ApplyBorders(
                    style,
                    overrides,
                    borderRules,
                    normalLineStyleId,
                    thinLineStyleId);
                style.SetCellStyleOverrideOptions(overrides);
                body.SetCellStyle(row, column, style);
            }
        }
    }

    private static FinishScheduleTextStyle ReadScheduleTextStyle(
        Document document,
        ElementId textTypeId,
        string role,
        double textSizeMillimeters,
        bool forceBold)
    {
        Element textType = document.GetElement(textTypeId)
            ?? throw new InvalidOperationException(
                $"Не найден тип текста для {role}.");
        Parameter fontParameter = GetRequiredTextTypeParameter(
            textType,
            role,
            BuiltInParameter.TEXT_FONT,
            BuiltInParameter.TEXT_STYLE_FONT);
        string fontName = fontParameter.AsString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fontName)
            || textSizeMillimeters <= 0
            || double.IsNaN(textSizeMillimeters)
            || double.IsInfinity(textSizeMillimeters))
        {
            throw new InvalidOperationException(
                $"Тип текста для {role} содержит недопустимый шрифт или размер.");
        }

        return new FinishScheduleTextStyle(
            fontName,
            FinishScheduleUnitAdapter.MillimetersToTableTextSize(textSizeMillimeters),
            forceBold || ReadTextStyleFlag(textType, BuiltInParameter.TEXT_STYLE_BOLD),
            ReadTextStyleFlag(textType, BuiltInParameter.TEXT_STYLE_ITALIC),
            ReadTextStyleFlag(textType, BuiltInParameter.TEXT_STYLE_UNDERLINE));
    }

    private static Parameter GetRequiredTextTypeParameter(
        Element textType,
        string role,
        params BuiltInParameter[] candidates)
    {
        foreach (BuiltInParameter candidate in candidates)
        {
            Parameter? parameter = textType.get_Parameter(candidate);
            if (parameter is not null)
            {
                return parameter;
            }
        }

        throw new InvalidOperationException(
            $"Тип текста для {role} не содержит обязательный параметр оформления.");
    }

    private static bool ReadTextStyleFlag(Element textType, BuiltInParameter parameterId)
    {
        Parameter? parameter = textType.get_Parameter(parameterId);
        return parameter is not null && parameter.AsInteger() != 0;
    }

    private static void SetTextStyleFlag(
        Element textType,
        BuiltInParameter parameterId,
        bool value)
    {
        Parameter parameter = textType.get_Parameter(parameterId)
            ?? throw new InvalidOperationException(
                $"Тип текста «{textType.Name}» не содержит параметр {parameterId}.");
        if (parameter.IsReadOnly)
        {
            throw new InvalidOperationException(
                $"Параметр {parameterId} типа текста «{textType.Name}» доступен только для чтения.");
        }

        parameter.Set(value ? 1 : 0);
    }

    private static void ApplyScheduleTextStyle(
        TableCellStyle style,
        TableCellStyleOverrideOptions overrides,
        FinishScheduleTextStyle textStyle)
    {
        style.FontName = textStyle.FontName;
        style.TextSize = textStyle.TextSize;
        style.IsFontBold = textStyle.IsBold;
        style.IsFontItalic = textStyle.IsItalic;
        style.IsFontUnderline = textStyle.IsUnderline;
        overrides.Font = true;
        overrides.FontSize = true;
        overrides.Bold = true;
        overrides.Italics = true;
        overrides.Underline = true;
    }

    private static void UseScheduleTextDefaults(TableCellStyleOverrideOptions overrides)
    {
        overrides.Font = false;
        overrides.FontSize = false;
        overrides.FontColor = false;
        overrides.Bold = false;
        overrides.Italics = false;
        overrides.Underline = false;
    }

    private static void ApplyBorders(
        TableCellStyle style,
        TableCellStyleOverrideOptions overrides,
        FinishScheduleCellBorderRules rules,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        ElementId top = ResolveLineStyle(rules.Top, normalLineStyleId, thinLineStyleId);
        ElementId bottom = ResolveLineStyle(rules.Bottom, normalLineStyleId, thinLineStyleId);
        ElementId left = ResolveLineStyle(rules.Left, normalLineStyleId, thinLineStyleId);
        ElementId right = ResolveLineStyle(rules.Right, normalLineStyleId, thinLineStyleId);
        if (top == ElementId.InvalidElementId
            || bottom == ElementId.InvalidElementId
            || left == ElementId.InvalidElementId
            || right == ElementId.InvalidElementId)
        {
            return;
        }

        style.BorderTopLineStyle = top;
        style.BorderBottomLineStyle = bottom;
        style.BorderLeftLineStyle = left;
        style.BorderRightLineStyle = right;
        // Revit 2022 requires the aggregate border override together with the
        // individual side overrides for custom header borders to render reliably.
        overrides.BorderLineStyle = true;
        overrides.BorderTopLineStyle = true;
        overrides.BorderBottomLineStyle = true;
        overrides.BorderLeftLineStyle = true;
        overrides.BorderRightLineStyle = true;
    }

    private static bool HasExpectedBorders(
        TableCellStyle style,
        TableCellStyleOverrideOptions overrides,
        FinishScheduleCellBorderRules rules,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        return overrides.BorderLineStyle
            && overrides.BorderTopLineStyle
            && overrides.BorderBottomLineStyle
            && overrides.BorderLeftLineStyle
            && overrides.BorderRightLineStyle
            && RevitElementIds.GetValue(style.BorderTopLineStyle)
                == RevitElementIds.GetValue(ResolveLineStyle(
                    rules.Top,
                    normalLineStyleId,
                    thinLineStyleId))
            && RevitElementIds.GetValue(style.BorderBottomLineStyle)
                == RevitElementIds.GetValue(ResolveLineStyle(
                    rules.Bottom,
                    normalLineStyleId,
                    thinLineStyleId))
            && RevitElementIds.GetValue(style.BorderLeftLineStyle)
                == RevitElementIds.GetValue(ResolveLineStyle(
                    rules.Left,
                    normalLineStyleId,
                    thinLineStyleId))
            && RevitElementIds.GetValue(style.BorderRightLineStyle)
                == RevitElementIds.GetValue(ResolveLineStyle(
                    rules.Right,
                    normalLineStyleId,
                    thinLineStyleId));
    }

    private static ElementId ResolveLineStyle(
        FinishScheduleLineWeight weight,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        return weight == FinishScheduleLineWeight.Normal
            ? normalLineStyleId
            : thinLineStyleId;
    }

    private static ElementId GetLineStyleCategoryId(
        Document document,
        string preferredName,
        BuiltInCategory fallbackCategory)
    {
        Category? lines = Category.GetCategory(document, BuiltInCategory.OST_Lines);
        if (lines is not null)
        {
            foreach (Category subcategory in lines.SubCategories)
            {
                if (!FinishRoomScheduleStyleRules.MatchesLineStyleName(
                        subcategory.Name,
                        preferredName))
                {
                    continue;
                }

                // Revit's schedule border editor persists the category id of the
                // line style, not the id of its projection GraphicsStyle element.
                return subcategory.Id;
            }
        }

        Category? category = Category.GetCategory(document, fallbackCategory);
        return category?.Id ?? ElementId.InvalidElementId;
    }

    private static void EnsureLineStyles(
        Document document,
        ElementId normalLineStyleId,
        ElementId thinLineStyleId)
    {
        if (normalLineStyleId == ElementId.InvalidElementId
            || Category.GetCategory(document, normalLineStyleId) is null)
        {
            throw new InvalidOperationException(
                $"Не найден стиль линий «{FinishRoomScheduleStyleRules.NormalLineStyleName}».");
        }

        if (thinLineStyleId == ElementId.InvalidElementId
            || Category.GetCategory(document, thinLineStyleId) is null)
        {
            throw new InvalidOperationException(
                $"Не найден стиль линий «{FinishRoomScheduleStyleRules.ThinLineStyleName}».");
        }

        if (RevitElementIds.GetValue(normalLineStyleId)
            == RevitElementIds.GetValue(thinLineStyleId))
        {
            throw new InvalidOperationException(
                "Для обычных и тонких границ ведомости Revit вернул один стиль линий.");
        }
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

internal sealed record FinishScheduleTextStyle(
    string FontName,
    double TextSize,
    bool IsBold,
    bool IsItalic,
    bool IsUnderline);

internal static class FinishScheduleUnitAdapter
{
    private const double MillimetersPerInch = 25.4;
    private const double TableTextUnitsPerInch = 96;

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

    public static double MillimetersToTableTextSize(double millimeters)
    {
        return millimeters * TableTextUnitsPerInch / MillimetersPerInch;
    }
}
