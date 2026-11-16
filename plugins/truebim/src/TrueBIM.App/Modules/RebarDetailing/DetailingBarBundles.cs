namespace TrueBIM.App.Modules.RebarDetailing;

/// <summary>Groups nearby transverse bars in the same visible column. Coordinates are millimetres.</summary>
public static class DetailingBarBundles
{
    public static IReadOnlyList<IReadOnlyList<int>> Create(IEnumerable<(int Id, double X, double Y, double Diameter)> source)
    {
        var columns = new List<List<(int Id, double X, double Y, double Diameter)>>();
        foreach (var point in source.OrderBy(p => p.X).ThenBy(p => p.Y).ThenBy(p => p.Id))
        {
            var column = columns.LastOrDefault();
            if (column == null || point.X - column[0].X > 1)
                columns.Add(column = new List<(int, double, double, double)>());
            column.Add(point);
        }
        var result = new List<IReadOnlyList<int>>();
        foreach (var column in columns)
        {
            List<int>? bundle = null;
            (double Y, double Diameter) previous = (double.NegativeInfinity, 0);
            foreach (var point in column.OrderBy(p => p.Y).ThenBy(p => p.Id))
            {
                // Up to 5 mm clear gap; ordinary 50/100/200 mm pitches stay
                // independent. Coincident axes are one measured row as before.
                double contact = point.Diameter > 0 && previous.Diameter > 0
                    ? (point.Diameter + previous.Diameter) / 2 + 5 : 0.1;
                if (bundle == null || point.Y - previous.Y > contact)
                {
                    bundle = new List<int>(); result.Add(bundle);
                }
                bundle.Add(point.Id); previous = (point.Y, point.Diameter);
            }
        }
        return result;
    }
}
