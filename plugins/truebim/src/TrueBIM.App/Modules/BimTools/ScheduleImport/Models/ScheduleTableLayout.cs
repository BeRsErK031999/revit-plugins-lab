namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleTableLayout(
    IReadOnlyList<double> ColumnWidthsFeet,
    IReadOnlyList<double> RowHeightsFeet)
{
    public double WidthFeet => ColumnWidthsFeet.Sum();

    public double HeightFeet => RowHeightsFeet.Sum();
}
