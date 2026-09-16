using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Фиксирует отображаемую таблицу в текстовых ячейках того же вида.</summary>
public sealed class FinishScheduleSnapshotService
{
    public static string? Validate(ViewSchedule schedule)
    {
        if (RevitFinishParameterAccess.IsOwnedByOtherUser(schedule.Document, schedule.Id))
        {
            return $"Прежняя ведомость «{schedule.Name}» занята другим пользователем. "
                   + "Освободите её, чтобы сохранить старые данные перед новым расчётом.";
        }

        if (typeof(ViewSchedule).GetMethod("IsSplit", Type.EmptyTypes)?.Invoke(schedule, null) is true)
        {
            return $"Прежняя ведомость «{schedule.Name}» разделена на части. "
                   + "Объедините её части перед созданием архивной версии. Данные пока не изменены.";
        }
        return null;
    }

    // The caller owns a transaction inside the same group as parameter writes.
    // Keeping the ViewSchedule id preserves its existing placements on sheets.
    public void Freeze(ViewSchedule schedule)
    {
        string? issue = Validate(schedule);
        if (issue is not null)
        {
            throw new InvalidOperationException(issue);
        }

        Document document = schedule.Document;
        document.Regenerate();
        schedule.RefreshData();
        using TableSnapshot snapshot = Capture(schedule);
        HideLiveRows(schedule);
        document.Regenerate();
        schedule.RefreshData();
        WriteSnapshot(schedule, snapshot);
        document.Regenerate();
        schedule.RefreshData();
        VerifySnapshot(schedule, snapshot);
    }

    private static TableSnapshot Capture(ViewSchedule schedule)
    {
        TableSnapshot snapshot = new();
        try
        {
            using TableData table = schedule.GetTableData();
            using TableSectionData body = table.GetSectionData(SectionType.Body);
            using TableSectionData header = table.GetSectionData(SectionType.Header);
            int columnCount = body.NumberOfColumns;
            if (columnCount == 0)
            {
                throw new InvalidOperationException($"Ведомость «{schedule.Name}» не содержит столбцов для сохранения.");
            }

            snapshot.Widths = Enumerable.Range(0, columnCount)
                .Select(index => body.GetColumnWidth(body.FirstColumnNumber + index)).ToArray();
            if (schedule.Definition.ShowTitle)
            {
                CaptureSection(schedule, header, SectionType.Header, snapshot, columnCount);
            }

            CaptureSection(schedule, body, SectionType.Body, snapshot, columnCount);
            if (snapshot.Rows.Count == 0)
            {
                throw new InvalidOperationException($"Ведомость «{schedule.Name}» не содержит строк для сохранения.");
            }

            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private static void CaptureSection(
        ViewSchedule schedule,
        TableSectionData section,
        SectionType type,
        TableSnapshot snapshot,
        int columnCount)
    {
        for (int row = section.FirstRowNumber; row <= section.LastRowNumber; row++)
        {
            if (type == SectionType.Body && Enumerable.Range(section.FirstColumnNumber, section.NumberOfColumns)
                    .All(column => string.IsNullOrWhiteSpace(schedule.GetCellText(type, row, column))
                        && section.GetMergedCell(row, column).Top == row && section.GetMergedCell(row, column).Bottom == row))
                continue;
            int targetRow = snapshot.Rows.Count;
            List<CellSnapshot> cells = [];
            snapshot.Rows.Add(new RowSnapshot(section.GetRowHeight(row), cells));
            for (int index = 0; index < section.NumberOfColumns; index++)
            {
                int column = section.FirstColumnNumber + index;
                if (section.GetCellType(row, column) == CellType.Graphic)
                {
                    throw new InvalidOperationException(
                        $"Ведомость «{schedule.Name}» содержит изображение в ячейке. "
                        + "Сохранить такую ячейку в текстовую архивную версию нельзя.");
                }

                TableMergedCell merged = section.GetMergedCell(row, column);
                if (merged.Top != row || merged.Left != column)
                {
                    continue;
                }

                int targetRight = merged.Right - section.FirstColumnNumber;
                if (section.NumberOfColumns == 1)
                {
                    targetRight = columnCount - 1;
                }

                using TableCellStyle source = section.GetTableCellStyle(row, column);
                TableCellStyle style = new(source);
                cells.Add(new CellSnapshot(
                    targetRow,
                    index,
                    targetRow + merged.Bottom - row,
                    targetRight,
                    schedule.GetCellText(type, row, column),
                    style));
                ElementId textTypeId = type == SectionType.Header
                    ? (row == section.FirstRowNumber ? schedule.TitleTextTypeId : schedule.HeaderTextTypeId)
                    : (schedule.Definition.ShowHeaders && row == section.FirstRowNumber
                        ? schedule.HeaderTextTypeId : schedule.BodyTextTypeId);
                FreezeTextDefaults(schedule, textTypeId, style);
            }
            if (type == SectionType.Body)
            {
                double height = cells.Where(cell => cell.Top == cell.Bottom).Select(cell =>
                    FinishSnapshotTextMeasurement.RequiredHeight(cell.Text, cell.Style,
                        snapshot.Widths.Skip(cell.Left).Take(cell.Right - cell.Left + 1).Sum()))
                    .DefaultIfEmpty(0).Max();
                snapshot.Rows[targetRow] = new RowSnapshot(Math.Max(snapshot.Rows[targetRow].Height, height), cells);
            }
        }
    }

    private static void FreezeTextDefaults(ViewSchedule schedule, ElementId typeId, TableCellStyle style)
    {
        Element textType = schedule.Document.GetElement(typeId);
        using TableCellStyleOverrideOptions options = style.GetCellStyleOverrideOptions();
        if (!options.Font)
        {
            style.FontName = textType.get_Parameter(BuiltInParameter.TEXT_FONT)?.AsString()
                             ?? textType.get_Parameter(BuiltInParameter.TEXT_STYLE_FONT)?.AsString()
                             ?? style.FontName;
        }

        if (!options.FontSize)
        {
            Parameter? size = textType.get_Parameter(BuiltInParameter.TEXT_SIZE);
            if (size is not null)
            {
                style.TextSize = size.AsDouble() * 12 * 96;
            }
        }

        if (!options.Bold)
        {
            style.IsFontBold = textType.get_Parameter(BuiltInParameter.TEXT_STYLE_BOLD)?.AsInteger() == 1;
        }

        if (!options.Italics)
        {
            style.IsFontItalic = textType.get_Parameter(BuiltInParameter.TEXT_STYLE_ITALIC)?.AsInteger() == 1;
        }

        if (!options.Underline)
        {
            style.IsFontUnderline = textType.get_Parameter(BuiltInParameter.TEXT_STYLE_UNDERLINE)?.AsInteger() == 1;
        }

        options.Font = options.FontSize = options.Bold = options.Italics = options.Underline = true;
        options.HorizontalAlignment = options.VerticalAlignment = true;
        options.BorderLineStyle = options.BorderTopLineStyle = options.BorderBottomLineStyle = true;
        options.BorderLeftLineStyle = options.BorderRightLineStyle = true;
        style.SetCellStyleOverrideOptions(options);
    }

    private static void HideLiveRows(ViewSchedule schedule)
    {
        ScheduleDefinition definition = schedule.Definition;
        for (int index = definition.GetFilterCount() - 1; index >= 0; index--)
        {
            definition.RemoveFilter(index);
        }

        for (int index = definition.GetSortGroupFieldCount() - 1; index >= 0; index--)
        {
            definition.RemoveSortGroupField(index);
        }

        ScheduleField? filterField = definition.GetFieldOrder().Select(definition.GetField)
            .FirstOrDefault(field => RevitElementIds.GetValue(field.ParameterId) == (long)BuiltInParameter.ROOM_NUMBER);
        if (filterField is null)
        {
            filterField = definition.AddField(ScheduleFieldType.Instance,
                RevitElementIds.Create((long)BuiltInParameter.ROOM_NUMBER));
            filterField.IsHidden = true;
        }

        // Contradictory filters keep the data section empty for any future model values.
        const string impossibleValue = "TrueBIM: archived finish schedule";
        definition.AddFilter(new ScheduleFilter(filterField.FieldId, ScheduleFilterType.Equal, impossibleValue));
        definition.AddFilter(new ScheduleFilter(filterField.FieldId, ScheduleFilterType.NotEqual, impossibleValue));
        definition.ShowHeaders = false;
        definition.ShowTitle = true;
        definition.ShowGrandTotal = false;
    }

    private static void WriteSnapshot(ViewSchedule schedule, TableSnapshot snapshot)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        for (int row = header.FirstRowNumber; row <= header.LastRowNumber; row++)
        {
            for (int column = header.FirstColumnNumber; column <= header.LastColumnNumber; column++)
            {
                header.SetMergedCell(row, column, new TableMergedCell(row, column, row, column));
                header.ClearCell(row, column);
            }
        }

        while (header.NumberOfRows > snapshot.Rows.Count)
        {
            header.RemoveRow(header.LastRowNumber);
        }

        while (header.NumberOfRows < snapshot.Rows.Count)
        {
            header.InsertRow(header.LastRowNumber + 1);
        }

        while (header.NumberOfColumns > snapshot.Widths.Length)
        {
            header.RemoveColumn(header.LastColumnNumber);
        }

        while (header.NumberOfColumns < snapshot.Widths.Length)
        {
            header.InsertColumn(header.LastColumnNumber);
        }

        for (int index = 0; index < snapshot.Widths.Length; index++)
        {
            header.SetColumnWidth(header.FirstColumnNumber + index, snapshot.Widths[index]);
        }

        for (int index = 0; index < snapshot.Rows.Count; index++)
        {
            RowSnapshot row = snapshot.Rows[index];
            header.SetRowHeight(header.FirstRowNumber + index, row.Height);
            foreach (CellSnapshot cell in row.Cells)
            {
                int top = header.FirstRowNumber + cell.Top;
                int left = header.FirstColumnNumber + cell.Left;
                if (cell.Top != cell.Bottom || cell.Left != cell.Right)
                {
                    header.MergeCells(new TableMergedCell(top, left,
                        header.FirstRowNumber + cell.Bottom, header.FirstColumnNumber + cell.Right));
                }

                header.SetCellText(top, left, NormalizeLineEndings(cell.Text));
                if (!header.AllowOverrideCellStyle(top, left))
                {
                    throw new InvalidOperationException($"Не удалось сохранить оформление ведомости «{schedule.Name}».");
                }

                header.SetCellStyle(top, left, cell.Style);
            }
        }
    }

    private static void VerifySnapshot(ViewSchedule schedule, TableSnapshot snapshot)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        foreach (CellSnapshot cell in snapshot.Rows.SelectMany(row => row.Cells))
        {
            string actual = header.GetCellText(header.FirstRowNumber + cell.Top, header.FirstColumnNumber + cell.Left);
            if (!string.Equals(NormalizeLineEndings(actual), NormalizeLineEndings(cell.Text), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Проверка архивных данных ведомости «{schedule.Name}» не пройдена.");
            }
        }

        if (new FilteredElementCollector(schedule.Document, schedule.Id)
            .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Any())
        {
            throw new InvalidOperationException($"Ведомость «{schedule.Name}» всё ещё зависит от параметров помещений.");
        }
    }

    private static string NormalizeLineEndings(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    private sealed record CellSnapshot(int Top, int Left, int Bottom, int Right, string Text, TableCellStyle Style);
    private sealed record RowSnapshot(double Height, List<CellSnapshot> Cells);

    private sealed class TableSnapshot : IDisposable
    {
        public double[] Widths { get; set; } = [];
        public List<RowSnapshot> Rows { get; } = [];
        public void Dispose()
        {
            foreach (CellSnapshot cell in Rows.SelectMany(row => row.Cells))
            {
                cell.Style.Dispose();
            }
        }
    }
}
