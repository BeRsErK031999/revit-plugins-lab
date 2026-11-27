namespace TrueBIM.App.Modules.SlabReinforcement;

// All plan coordinates and distances are in millimetres in the reinforcement axes.
public sealed record OpeningBox(double X0, double X1, double Y0, double Y1)
{
    internal OpeningBox AcrossY() => new(Y0, Y1, X0, X1);
    internal OpeningBox Union(OpeningBox other) => new(Math.Min(X0, other.X0), Math.Max(X1, other.X1),
        Math.Min(Y0, other.Y0), Math.Max(Y1, other.Y1));
    public double Gap(OpeningBox other)
    {
        double x = Math.Max(0, Math.Max(other.X0 - X1, X0 - other.X1));
        double y = Math.Max(0, Math.Max(other.Y0 - Y1, Y0 - other.Y1));
        return Math.Sqrt(x * x + y * y);
    }
}

public sealed record OpeningRow(double Station, double Start, double End);
public static class OpeningLayout
{
    private const double Noise = 0.001;

    public static List<OpeningBox> Groups(IReadOnlyList<OpeningBox> holes, Func<int, int, double> distance)
    {
        var pending = new HashSet<int>(Enumerable.Range(0, holes.Count));
        var groups = new List<OpeningBox>();
        for (int seed = 0; seed < holes.Count; seed++)
        {
            if (!pending.Remove(seed)) continue;
            var members = new Queue<int>();
            members.Enqueue(seed);
            var box = holes[seed];
            while (members.Count > 0)
            {
                int member = members.Dequeue();
                foreach (int next in pending.ToArray())
                {
                    if (holes[member].Gap(holes[next]) >= 150 - Noise || distance(member, next) >= 150 - Noise) continue;
                    members.Enqueue(next);
                    pending.Remove(next);
                    box = box.Union(holes[next]);
                }
            }
            if (Math.Max(box.X1 - box.X0, box.Y1 - box.Y0) > 300 + Noise) groups.Add(box);
        }
        return groups;
    }

    public static List<OpeningRow> Rows(IReadOnlyList<OpeningBox> groups, bool alongY, double a, double b, double anchor)
    {
        var boxes = groups.Select(g => alongY ? g.AcrossY() : g).ToList();
        var rows = new List<OpeningRow>();
        foreach (int sign in new[] { -1, 1 })
        {
            // Only sides facing the same way and lying alongside one another can share exterior bars.
            var faces = boxes.Select(box => new OpeningRow(sign < 0 ? box.Y0 : box.Y1, box.X0, box.X1))
                .OrderByDescending(face => sign * face.Station).ThenBy(face => face.Start).ToList();
            while (faces.Count > 0)
            {
                var first = faces[0];
                faces.RemoveAt(0);
                var connected = new List<OpeningRow> { first };
                for (int i = 0; i < connected.Count; i++)
                {
                    var otherSide = connected[i];
                    for (int j = faces.Count - 1; j >= 0; j--)
                    {
                        var next = faces[j];
                        double gap = Math.Max(next.Start - otherSide.End, otherSide.Start - next.End);
                        if (Math.Abs(first.Station - next.Station) > 150 + Noise || gap < -Noise || gap > 2 * anchor + Noise) continue;
                        connected.Add(next);
                        faces.RemoveAt(j);
                    }
                }
                double start = connected.Min(f => f.Start), end = connected.Max(f => f.End);
                rows.Add(new(first.Station + sign * a, start, end));
                rows.Add(new(first.Station + sign * (a + b), start, end));
            }
        }

        // Facing sides in a narrow pier may request the same two bars. Keep the longest at each station.
        var kept = new List<OpeningRow>();
        foreach (var row in rows.OrderByDescending(r => r.End - r.Start).ThenBy(r => r.Start))
        {
            if (kept.Any(r => Math.Abs(r.Station - row.Station) < Noise &&
                r.Start - anchor <= row.End + anchor && row.Start - anchor <= r.End + anchor)) continue;
            kept.Add(row);
        }
        return kept;
    }
}
