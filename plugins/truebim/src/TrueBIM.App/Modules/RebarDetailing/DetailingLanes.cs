namespace TrueBIM.App.Modules.RebarDetailing;

/// <summary>Assigns drawing rows while letting disjoint dimension ranges share a row.</summary>
public sealed class DetailingLanes
{
    private readonly double clearance;
    private readonly List<List<(double Min, double Max)>> lanes = new();
    public int Count => lanes.Count;
    public DetailingLanes(double clearance) { this.clearance = Math.Max(0, clearance); }
    public int Reserve(double start, double end)
    {
        double min = Math.Min(start, end), max = Math.Max(start, end);
        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i].Any(range => max + clearance >= range.Min && min - clearance <= range.Max)) continue;
            lanes[i].Add((min, max)); return i;
        }
        lanes.Add(new List<(double, double)> { (min, max) }); return lanes.Count - 1;
    }
}
