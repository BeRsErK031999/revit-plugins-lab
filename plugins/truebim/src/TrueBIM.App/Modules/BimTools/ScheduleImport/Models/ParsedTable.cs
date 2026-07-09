namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ParsedTable(
    string SourceFilePath,
    int PageNumber,
    IReadOnlyList<ParsedRow> Rows,
    IReadOnlyList<string> Columns,
    IReadOnlyList<ParsedCell> Cells,
    double Confidence,
    IReadOnlyList<string> Warnings)
{
    public int RowCount => Rows.Count > 0
        ? Rows.Count
        : Cells.Count == 0
            ? 0
            : Cells.Max(cell => cell.RowIndex + Math.Max(1, cell.RowSpan));

    public int ColumnCount => Columns.Count > 0
        ? Columns.Count
        : Cells.Count == 0
            ? 0
            : Cells.Max(cell => cell.ColumnIndex + Math.Max(1, cell.ColumnSpan));

    public bool IsEmpty => RowCount == 0 || ColumnCount == 0 || Cells.Count == 0;
}
