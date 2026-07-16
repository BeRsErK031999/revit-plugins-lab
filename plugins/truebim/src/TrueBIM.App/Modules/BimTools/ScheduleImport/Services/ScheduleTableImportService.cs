using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleTableImportService
{
    private const double BaseTextSizeMm = 2.5;

    private readonly ScheduleTableLayoutService layoutService;
    private readonly ParsedTableValidationService validationService;
    private readonly ITrueBimLogger logger;

    public ScheduleTableImportService(
        ScheduleTableLayoutService layoutService,
        ParsedTableValidationService validationService,
        ITrueBimLogger logger)
    {
        this.layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        this.validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ScheduleImportCreationResult CreateSchedule(
        Document document,
        ParsedTable table,
        ImportOptions options)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(table, nameof(table));
        Guard.NotNull(options, nameof(options));

        ParsedTableValidationResult validation = validationService.Validate(table);
        if (!validation.Succeeded)
        {
            return new ScheduleImportCreationResult(
                "Спецификация не создана",
                null,
                false,
                0,
                0,
                validation.Warnings,
                validation.Errors);
        }

        List<string> warnings = [.. validation.Warnings, .. table.Warnings];
        ViewSchedule? schedule = null;
        using Transaction transaction = new(document, "TrueBIM: создание спецификации из таблицы");
        transaction.Start();
        try
        {
            schedule = ViewSchedule.CreateSchedule(document, ElementId.InvalidElementId);
            schedule.Name = CreateUniqueScheduleName(document, table.SourceFilePath);

            ScheduleDefinition definition = schedule.Definition;
            definition.ShowTitle = true;
            definition.ShowHeaders = false;
#if REVIT2022_OR_GREATER
            definition.ShowGridLines = true;
#endif
            ConfigureEmptyBody(definition);
            document.Regenerate();

            TableSectionData header = schedule.GetTableData().GetSectionData(SectionType.Header);
            UnmergeHeaderCells(schedule, header);
            header.NumberOfRows = table.RowCount;
            header.NumberOfColumns = table.ColumnCount;
            header.HideSection = false;

            ScheduleTableLayout layout = layoutService.CreateLayout(table, options.TableScale);
            ApplyDimensions(header, layout);
            ApplyCells(header, table, options.TableScale, warnings);

            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }

        warnings.Add("Импортированная таблица является статическим содержимым спецификации и не изменяет параметры элементов модели.");
        logger.Info($"Schedule Import created ViewSchedule '{schedule.Name}'. Rows={table.RowCount}; Columns={table.ColumnCount}; Source='{System.IO.Path.GetFileName(table.SourceFilePath)}'.");
        return new ScheduleImportCreationResult(
            schedule.Name,
            RevitElementIds.GetValue(schedule.Id),
            false,
            table.RowCount,
            table.ColumnCount,
            warnings.Distinct(StringComparer.CurrentCulture).ToList(),
            Array.Empty<string>());
    }

    private static void ConfigureEmptyBody(ScheduleDefinition definition)
    {
        if (!definition.CanFilter())
        {
            throw new InvalidOperationException("Revit не разрешил создать служебный фильтр для пустого тела спецификации.");
        }

        long[] candidateParameterIds =
        [
            (long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
            (long)BuiltInParameter.ALL_MODEL_MARK
        ];
        SchedulableField? filterField = definition.GetSchedulableFields()
            .FirstOrDefault(candidate => candidateParameterIds.Contains(RevitElementIds.GetValue(candidate.ParameterId)));
        if (filterField is null)
        {
            throw new InvalidOperationException("В новой спецификации Revit не найдено текстовое поле для служебного фильтра.");
        }

        ScheduleField field = definition.AddField(filterField);
        field.IsHidden = true;
        string filterToken = $"__TRUEBIM_IMPORTED_SCHEDULE_{Guid.NewGuid():N}__";
        using ScheduleFilter filter = new(field.FieldId, ScheduleFilterType.Equal, filterToken);
        definition.AddFilter(filter);
    }

    private static string CreateUniqueScheduleName(Document document, string sourceFilePath)
    {
        string sourceName = System.IO.Path.GetFileNameWithoutExtension(sourceFilePath);
        string baseName = NormalizeScheduleName(string.IsNullOrWhiteSpace(sourceName)
            ? "TrueBIM_Импорт таблицы"
            : $"TrueBIM_{sourceName}");
        HashSet<string> names = new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = $"{baseName} ({index})";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {Guid.NewGuid():N}";
    }

    private static string NormalizeScheduleName(string value)
    {
        char[] forbiddenCharacters = ['\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~'];
        string normalized = forbiddenCharacters.Aggregate(value, (current, character) => current.Replace(character, '_'));
        normalized = string.Join(" ", normalized.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length > 120)
        {
            normalized = normalized.Substring(0, 120).TrimEnd();
        }

        return string.IsNullOrWhiteSpace(normalized) ? "TrueBIM_Импорт таблицы" : normalized;
    }

    private static void UnmergeHeaderCells(ViewSchedule schedule, TableSectionData header)
    {
        HashSet<(int Top, int Left, int Bottom, int Right)> mergedCells = [];
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                using TableMergedCell merged = header.GetMergedCell(row, column);
                if (merged.Top == merged.Bottom && merged.Left == merged.Right)
                {
                    continue;
                }

                mergedCells.Add((merged.Top, merged.Left, merged.Bottom, merged.Right));
            }
        }

        foreach ((int top, int left, int bottom, int right) in mergedCells)
        {
            if (schedule.CanUngroupHeaders(top, left, bottom, right))
            {
                schedule.UngroupHeaders(top, left, bottom, right);
            }
        }
    }

    private static void ApplyDimensions(TableSectionData header, ScheduleTableLayout layout)
    {
        for (int column = 0; column < layout.ColumnWidthsFeet.Count; column++)
        {
            header.SetColumnWidth(header.FirstColumnNumber + column, layout.ColumnWidthsFeet[column]);
        }

        for (int row = 0; row < layout.RowHeightsFeet.Count; row++)
        {
            header.SetRowHeight(header.FirstRowNumber + row, layout.RowHeightsFeet[row]);
        }
    }

    private static void ApplyCells(
        TableSectionData header,
        ParsedTable table,
        double tableScale,
        List<string> warnings)
    {
        for (int row = 0; row < table.RowCount; row++)
        {
            for (int column = 0; column < table.ColumnCount; column++)
            {
                int targetRow = header.FirstRowNumber + row;
                int targetColumn = header.FirstColumnNumber + column;
                header.ClearCell(targetRow, targetColumn);
                header.SetCellText(targetRow, targetColumn, GetCellText(table, row, column));
            }
        }

        foreach (ParsedCell cell in table.Cells.Where(cell => cell.RowSpan > 1 || cell.ColumnSpan > 1))
        {
            int top = header.FirstRowNumber + cell.RowIndex;
            int left = header.FirstColumnNumber + cell.ColumnIndex;
            int bottom = Math.Min(header.LastRowNumber, top + cell.RowSpan - 1);
            int right = Math.Min(header.LastColumnNumber, left + cell.ColumnSpan - 1);
            if (bottom > top || right > left)
            {
                using TableMergedCell merged = new(top, left, bottom, right);
                header.MergeCells(merged);
                header.SetCellText(top, left, cell.Text ?? string.Empty);
            }
        }

        bool styleWarningAdded = false;
        double normalizedScale = double.IsNaN(tableScale) || double.IsInfinity(tableScale) || tableScale <= 0
            ? 1
            : Math.Min(10, tableScale);
        for (int row = 0; row < table.RowCount; row++)
        {
            bool sectionRow = row > 0 && CountNonEmptyCells(table, row) == 1;
            for (int column = 0; column < table.ColumnCount; column++)
            {
                int targetRow = header.FirstRowNumber + row;
                int targetColumn = header.FirstColumnNumber + column;
                if (!header.AllowOverrideCellStyle(targetRow, targetColumn))
                {
                    if (!styleWarningAdded)
                    {
                        warnings.Add("Revit не разрешил переопределить оформление части ячеек; текст и размеры таблицы всё равно импортированы.");
                        styleWarningAdded = true;
                    }

                    continue;
                }

                using TableCellStyle style = header.GetTableCellStyle(targetRow, targetColumn);
                using TableCellStyleOverrideOptions overrides = style.GetCellStyleOverrideOptions();
                overrides.FontSize = true;
                overrides.Bold = true;
                overrides.Underline = true;
                overrides.HorizontalAlignment = true;
                overrides.VerticalAlignment = true;
                style.TextSize = ScheduleTableLayoutService.ToFeet(BaseTextSizeMm * normalizedScale);
                style.IsFontBold = row == 0 || sectionRow;
                style.IsFontUnderline = sectionRow;
                style.FontHorizontalAlignment = row == 0 || sectionRow || IsCompactValue(GetCellText(table, row, column))
                    ? HorizontalAlignmentStyle.Center
                    : HorizontalAlignmentStyle.Left;
                style.FontVerticalAlignment = VerticalAlignmentStyle.Middle;
                style.SetCellStyleOverrideOptions(overrides);
                header.SetCellStyle(targetRow, targetColumn, style);
            }
        }
    }

    private static string GetCellText(ParsedTable table, int row, int column)
    {
        ParsedCell? cell = table.Cells.FirstOrDefault(candidate =>
            candidate.RowIndex == row && candidate.ColumnIndex == column);
        if (cell is not null)
        {
            return cell.Text ?? string.Empty;
        }

        ParsedRow? parsedRow = table.Rows.FirstOrDefault(candidate => candidate.RowIndex == row);
        return parsedRow is not null && column < parsedRow.Values.Count
            ? parsedRow.Values[column] ?? string.Empty
            : string.Empty;
    }

    private static int CountNonEmptyCells(ParsedTable table, int row)
    {
        return Enumerable.Range(0, table.ColumnCount)
            .Count(column => !string.IsNullOrWhiteSpace(GetCellText(table, row, column)));
    }

    private static bool IsCompactValue(string text)
    {
        string value = text?.Trim() ?? string.Empty;
        return value.Length <= 12 && value.IndexOf(Environment.NewLine, StringComparison.Ordinal) < 0;
    }
}
