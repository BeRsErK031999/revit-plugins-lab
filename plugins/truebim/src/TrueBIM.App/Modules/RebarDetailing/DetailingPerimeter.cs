namespace TrueBIM.App.Modules.RebarDetailing;

/// <summary>Concrete intervals on a closed perimeter, starting at its upper left corner.</summary>
public static class DetailingPerimeter
{
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Trace(DetailingRect box,
        IEnumerable<(int Side, double Start, double End)> concrete, double tolerance = 0.1)
    {
        if (box.Width <= tolerance || box.Height <= tolerance) return Array.Empty<IReadOnlyList<(double, double)>>();
        double[] corners = { 0, box.Height, box.Height + box.Width, 2 * box.Height + box.Width, 2 * (box.Height + box.Width) };
        double length = corners[4];
        var intervals = concrete.Select(p =>
        {
            if (p.Side < 0 || p.Side > 3) throw new ArgumentOutOfRangeException(nameof(concrete));
            double sideLength = corners[p.Side + 1] - corners[p.Side];
            return (Start: corners[p.Side] + Math.Max(0, Math.Min(sideLength, Math.Min(p.Start, p.End))),
                End: corners[p.Side] + Math.Max(0, Math.Min(sideLength, Math.Max(p.Start, p.End))));
        }).Where(p => p.End - p.Start > tolerance).OrderBy(p => p.Start).ToList();
        var merged = new List<(double Start, double End)>();
        foreach (var interval in intervals)
        {
            if (merged.Count == 0 || interval.Start > merged[merged.Count - 1].End + tolerance) merged.Add(interval);
            else merged[merged.Count - 1] = (merged[merged.Count - 1].Start, Math.Max(merged[merged.Count - 1].End, interval.End));
        }
        if (merged.Count > 1 && merged[0].Start <= tolerance && merged[merged.Count - 1].End >= length - tolerance)
        {
            var first = merged[0];
            merged[merged.Count - 1] = (merged[merged.Count - 1].Start, length + first.End);
            merged.RemoveAt(0);
        }
        var paths = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (var interval in merged)
        {
            var distances = new List<double> { interval.Start };
            distances.AddRange(corners.Concat(corners.Select(c => c + length)).Distinct()
                .Where(c => c > interval.Start + tolerance && c < interval.End - tolerance).OrderBy(c => c));
            distances.Add(interval.End);
            paths.Add(distances.Select(Point).ToList());
        }
        return paths;

        (double X, double Y) Point(double distance)
        {
            double d = distance % length;
            if (d <= corners[1]) return (box.Left, box.Top - d);
            if (d <= corners[2]) return (box.Left + d - corners[1], box.Bottom);
            if (d <= corners[3]) return (box.Right, box.Bottom + d - corners[2]);
            return (box.Right - d + corners[3], box.Top);
        }
    }
}
