namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ParsedCell(
    int RowIndex,
    int ColumnIndex,
    int RowSpan,
    int ColumnSpan,
    string Text,
    ParsedCellBoundingBox? BoundingBox,
    double Confidence,
    bool IsHeader);
