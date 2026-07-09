namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record DraftingTableLayout(
    IReadOnlyList<double> ColumnWidthsFeet,
    IReadOnlyList<double> RowHeightsFeet,
    IReadOnlyList<DraftingTableCellLayout> Cells)
{
    public double WidthFeet => ColumnWidthsFeet.Sum();

    public double HeightFeet => RowHeightsFeet.Sum();
}

public sealed record DraftingTableCellLayout(
    ParsedCell Cell,
    double XFeet,
    double YFeet,
    double WidthFeet,
    double HeightFeet);
