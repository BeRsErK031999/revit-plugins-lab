using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class DraftingTableLayoutService
{
    public const double FeetPerMillimeter = 1.0 / 304.8;

    private const double MinimumColumnWidthMm = 24;
    private const double MaximumColumnWidthMm = 95;
    private const double MinimumRowHeightMm = 7.5;
    private const double HeaderRowHeightMm = 9.5;
    private const double CharacterWidthMm = 2.2;
    private const double HorizontalPaddingMm = 4;
    private const double VerticalPaddingMm = 2.5;

    public DraftingTableLayout CreateLayout(ParsedTable table, double tableScale)
    {
        Guard.NotNull(table, nameof(table));

        double scale = NormalizeScale(tableScale);
        IReadOnlyList<double> columnWidths = CalculateColumnWidths(table, scale);
        IReadOnlyList<double> rowHeights = CalculateRowHeights(table, scale);
        List<DraftingTableCellLayout> cells = [];

        foreach (ParsedCell cell in table.Cells)
        {
            if (cell.RowIndex >= rowHeights.Count || cell.ColumnIndex >= columnWidths.Count)
            {
                continue;
            }

            double x = columnWidths.Take(cell.ColumnIndex).Sum();
            double y = rowHeights.Take(cell.RowIndex).Sum();
            int columnSpan = Math.Min(Math.Max(1, cell.ColumnSpan), columnWidths.Count - cell.ColumnIndex);
            int rowSpan = Math.Min(Math.Max(1, cell.RowSpan), rowHeights.Count - cell.RowIndex);
            cells.Add(new DraftingTableCellLayout(
                cell,
                x,
                y,
                columnWidths.Skip(cell.ColumnIndex).Take(columnSpan).Sum(),
                rowHeights.Skip(cell.RowIndex).Take(rowSpan).Sum()));
        }

        return new DraftingTableLayout(columnWidths, rowHeights, cells);
    }

    public static double ToFeet(double millimeters)
    {
        return millimeters * FeetPerMillimeter;
    }

    private static IReadOnlyList<double> CalculateColumnWidths(ParsedTable table, double scale)
    {
        if (CanUseBoundingBoxProportions(table))
        {
            double[] rawWidths = new double[table.ColumnCount];
            foreach (ParsedCell cell in table.Cells.Where(cell => cell.BoundingBox is not null))
            {
                rawWidths[cell.ColumnIndex] = Math.Max(rawWidths[cell.ColumnIndex], cell.BoundingBox!.Width);
            }

            double targetWidth = table.ColumnCount * ToFeet(42 * scale);
            double rawTotal = rawWidths.Sum();
            if (rawTotal > 0)
            {
                return rawWidths
                    .Select(width => ClampFeet(targetWidth * width / rawTotal, MinimumColumnWidthMm * scale, MaximumColumnWidthMm * scale))
                    .ToList();
            }
        }

        List<double> widths = [];
        for (int columnIndex = 0; columnIndex < table.ColumnCount; columnIndex++)
        {
            int maxLength = table.Cells
                .Where(cell => cell.ColumnIndex == columnIndex)
                .Select(cell => cell.Text?.Length ?? 0)
                .DefaultIfEmpty(0)
                .Max();
            double widthMm = maxLength * CharacterWidthMm + HorizontalPaddingMm * 2;
            widths.Add(ClampFeet(ToFeet(widthMm * scale), MinimumColumnWidthMm * scale, MaximumColumnWidthMm * scale));
        }

        return widths;
    }

    private static IReadOnlyList<double> CalculateRowHeights(ParsedTable table, double scale)
    {
        List<double> heights = [];
        for (int rowIndex = 0; rowIndex < table.RowCount; rowIndex++)
        {
            bool isHeader = table.Cells.Any(cell => cell.RowIndex == rowIndex && cell.IsHeader);
            int maxLines = table.Cells
                .Where(cell => cell.RowIndex == rowIndex)
                .Select(cell => Math.Max(1, (cell.Text ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None).Length))
                .DefaultIfEmpty(1)
                .Max();
            double baseHeight = isHeader ? HeaderRowHeightMm : MinimumRowHeightMm;
            heights.Add(ToFeet((baseHeight + (maxLines - 1) * 4 + VerticalPaddingMm) * scale));
        }

        return heights;
    }

    private static bool CanUseBoundingBoxProportions(ParsedTable table)
    {
        return table.ColumnCount > 0
            && table.Cells.Count(cell => cell.BoundingBox is not null && cell.BoundingBox.Width > 0) >= table.ColumnCount;
    }

    private static double ClampFeet(double valueFeet, double minimumMillimeters, double maximumMillimeters)
    {
        double minimumFeet = ToFeet(minimumMillimeters);
        double maximumFeet = ToFeet(maximumMillimeters);
        if (valueFeet < minimumFeet)
        {
            return minimumFeet;
        }

        return valueFeet > maximumFeet ? maximumFeet : valueFeet;
    }

    private static double NormalizeScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1;
        }

        return Math.Min(scale, 10);
    }
}
