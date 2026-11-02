namespace TrueBIM.App.Modules.RebarDetailing;

/// <summary>Separates uniform arrays from the adjacent bars with a different pitch.</summary>
public static class DetailingBarGroups
{
    public static IReadOnlyList<IReadOnlyList<int>> Create(IEnumerable<(int Id, IReadOnlyList<double> Rows)> source, double tolerance = 0.1)
    {
        var arrays = source.Select(a => (a.Id, Rows: Unique(a.Rows, tolerance))).Where(a => a.Rows.Count > 0).ToList();
        var regular = arrays.Where(a => a.Rows.Count >= 3 && !DetailingSectionLayout.HasIrregularPitch(a.Rows, tolerance))
            .OrderBy(a => a.Rows[0]).ThenBy(a => a.Id).ToList();
        var groups = new List<(List<int> Ids, double Min, double Max, double Pitch)>();
        var assigned = new HashSet<int>();
        foreach (var array in regular)
        {
            double pitch = array.Rows[1] - array.Rows[0];
            int match = groups.FindIndex(g => Math.Abs(g.Pitch - pitch) <= tolerance
                && array.Rows[0] <= g.Max + pitch + tolerance && array.Rows[array.Rows.Count - 1] >= g.Min - pitch - tolerance
                && OnGrid(array.Rows[0], g.Min, pitch, tolerance));
            if (match < 0) groups.Add((new List<int> { array.Id }, array.Rows[0], array.Rows[array.Rows.Count - 1], pitch));
            else
            {
                var group = groups[match]; group.Ids.Add(array.Id);
                groups[match] = (group.Ids, Math.Min(group.Min, array.Rows[0]), Math.Max(group.Max, array.Rows[array.Rows.Count - 1]), pitch);
            }
            assigned.Add(array.Id);
        }
        // A single bar can continue an array, but only on its measured grid.
        // Ambiguous bars and nonuniform sets retain a separate counted label.
        bool extended;
        do
        {
            extended = false;
            foreach (var array in arrays.Where(a => !assigned.Contains(a.Id) && a.Rows.Count <= 2))
            {
                var matches = groups.Select((g, index) => (Group: g, Index: index)).Where(p =>
                    array.Rows.All(y => y >= p.Group.Min - p.Group.Pitch - tolerance && y <= p.Group.Max + p.Group.Pitch + tolerance
                        && OnGrid(y, p.Group.Min, p.Group.Pitch, tolerance))).ToList();
                if (matches.Count != 1) continue;
                var match = matches[0]; var group = match.Group; group.Ids.Add(array.Id);
                groups[match.Index] = (group.Ids, Math.Min(group.Min, array.Rows[0]), Math.Max(group.Max, array.Rows[array.Rows.Count - 1]), group.Pitch);
                assigned.Add(array.Id); extended = true;
            }
        } while (extended);
        var result = groups.Select(g => (IReadOnlyList<int>)g.Ids).ToList();
        // Do not join irregular bars across an intervening regular field.
        foreach (var remaining in arrays.Where(a => !assigned.Contains(a.Id)).GroupBy(a =>
            string.Join("|", groups.Select(g => a.Rows[a.Rows.Count - 1] < g.Min - tolerance ? "below" : a.Rows[0] > g.Max + tolerance ? "above" : "overlap"))))
            result.Add(remaining.Select(a => a.Id).ToList());
        return result;
    }

    public static IReadOnlyList<double> Unique(IEnumerable<double> source, double tolerance = 0.1)
    {
        var result = new List<double>();
        foreach (double value in source.OrderBy(v => v))
            if (result.Count == 0 || value - result[result.Count - 1] > tolerance) result.Add(value);
        return result;
    }

    private static bool OnGrid(double value, double origin, double pitch, double tolerance) =>
        Math.Abs(value - origin - Math.Round((value - origin) / pitch) * pitch) <= tolerance;
}
