using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleTableImportService
{
    private const double BaseTextSizeMm = 2.5;

    private readonly DraftingTableLayoutService layoutService;
    private readonly ParsedTableValidationService validationService;
    private readonly ITrueBimLogger logger;

    public ScheduleTableImportService(
        DraftingTableLayoutService layoutService,
        ParsedTableValidationService validationService,
        ITrueBimLogger logger)
    {
        this.layoutService = layoutService ?? throw new ArgumentNullException(nameof(layoutService));
        this.validationService = validationService ?? throw new ArgumentNullException(nameof(validationService));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ScheduleTableImportResult Apply(
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
            return new ScheduleTableImportResult(
                "Спецификация не выбрана",
                0,
                0,
                validation.Warnings,
                validation.Errors);
        }

        ViewSchedule? schedule = options.TargetScheduleId is > 0
            ? document.GetElement(RevitElementIds.Create(options.TargetScheduleId.Value)) as ViewSchedule
            : null;
        if (schedule is null || schedule.IsTemplate)
        {
            return new ScheduleTableImportResult(
                "Спецификация не выбрана",
                0,
                0,
                validation.Warnings,
                ["Выбранная спецификация Revit не найдена или является шаблоном вида."]);
        }

        List<string> warnings = [.. validation.Warnings, .. table.Warnings];
        using Transaction transaction = new(document, "TrueBIM: импорт в спецификацию");
        transaction.Start();
        try
        {
            ScheduleDefinition definition = schedule.Definition;
            definition.ShowTitle = true;
            definition.ShowHeaders = false;
#if REVIT2022_OR_GREATER
            definition.ShowGridLines = true;
#endif

            TableData tableData = schedule.GetTableData();
            TableSectionData header = tableData.GetSectionData(SectionType.Header);
            TableSectionData body = tableData.GetSectionData(SectionType.Body);
            UnmergeHeaderCells(schedule, header);

            header.NumberOfRows = table.RowCount;
            header.NumberOfColumns = table.ColumnCount;
            header.HideSection = false;
            body.HideSection = true;

            DraftingTableLayout layout = layoutService.CreateLayout(table, options.TableScale);
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

        logger.Info($"Schedule Import replaced schedule header. Schedule='{schedule.Name}'; Rows={table.RowCount}; Columns={table.ColumnCount}; Source='{System.IO.Path.GetFileName(table.SourceFilePath)}'.");
        return new ScheduleTableImportResult(
            schedule.Name,
            table.RowCount,
            table.ColumnCount,
            warnings.Distinct(StringComparer.CurrentCulture).ToList(),
            Array.Empty<string>());
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

    private static void ApplyDimensions(TableSectionData header, DraftingTableLayout layout)
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
                style.TextSize = DraftingTableLayoutService.ToFeet(BaseTextSizeMm * normalizedScale);
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
