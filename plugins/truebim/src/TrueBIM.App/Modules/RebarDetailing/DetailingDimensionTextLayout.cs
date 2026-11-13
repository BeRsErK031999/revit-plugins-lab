namespace TrueBIM.App.Modules.RebarDetailing;

public readonly struct DetailingDimensionLabel
{
    public double Center { get; }
    public double Length { get; }
    public double Width { get; }
    public DetailingDimensionLabel(double center, double length, double width)
    { Center = center; Length = length; Width = width; }
}

public readonly struct DetailingDimensionTextPlacement
{
    public double Center { get; }
    public int Row { get; }
    public DetailingDimensionTextPlacement(double center, int row) { Center = center; Row = row; }
}

public static class DetailingDimensionTextLayout
{
    // Coordinates and text widths use the same units. A fitting label remains
    // centered. Only a cramped label moves outside its witness lines.
    public static IReadOnlyList<DetailingDimensionTextPlacement> Place(IReadOnlyList<DetailingDimensionLabel> labels,
        IReadOnlyList<(double Center, double Width)> formulas, double clearance, double tickClearance)
    {
        var result = new List<DetailingDimensionTextPlacement>();
        for (int index = 0; index < labels.Count; index++)
        {
            var label = labels[index];
            bool fits = label.Width + 2 * clearance <= label.Length;
            double center = label.Center;
            int row = 0;
            if (!fits)
            {
                bool lower = index == 0;
                center += (lower ? -1 : 1) * (label.Length / 2 + label.Width / 2 + tickClearance);
                // Interior short segments need a second text row instead of
                // pushing all following, otherwise perfectly fitting, labels.
                if (index > 0 && index < labels.Count - 1) row = 1;
                while (formulas.Any(f => row == 0 && Math.Abs(center - f.Center) < (label.Width + f.Width) / 2 + clearance)
                    || Enumerable.Range(0, result.Count).Any(i => result[i].Row == row
                        && Math.Abs(center - result[i].Center) < (label.Width + labels[i].Width) / 2 + clearance)
                    || Enumerable.Range(index + 1, labels.Count - index - 1).Any(i => row == 0
                        && labels[i].Width + 2 * clearance <= labels[i].Length
                        && Math.Abs(center - labels[i].Center) < (label.Width + labels[i].Width) / 2 + clearance)) row++;
            }
            result.Add(new DetailingDimensionTextPlacement(center, row));
        }
        return result;
    }
}
