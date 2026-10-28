namespace TrueBIM.App.Modules.RebarDetailing;

public sealed class DetailingChainPoint
{
    public int Id { get; }
    public double PositionMm { get; }
    public string RunKey { get; }
    public bool Boundary { get; }
    public DetailingChainPoint(int id, double positionMm, string runKey, bool boundary = false)
    { Id = id; PositionMm = positionMm; RunKey = runKey; Boundary = boundary; }
}

public sealed class DetailingChainSpan
{
    public DetailingChainPoint Start { get; }
    public DetailingChainPoint End { get; }
    public int Intervals { get; }
    public double PitchMm { get; }
    public DetailingChainSpan(DetailingChainPoint start, DetailingChainPoint end, int intervals, double pitchMm)
    { Start = start; End = end; Intervals = intervals; PitchMm = pitchMm; }
}

public sealed class DetailingChainPlan
{
    public IReadOnlyList<DetailingChainPoint> Points { get; }
    public IReadOnlyList<DetailingChainSpan> EqualSpans { get; }
    private DetailingChainPlan(IReadOnlyList<DetailingChainPoint> points, IReadOnlyList<DetailingChainSpan> equalSpans)
    { Points = points; EqualSpans = equalSpans; }

    public static DetailingChainPlan Create(IEnumerable<DetailingChainPoint> source, int minimumIntervals = 4, double toleranceMm = 0.1)
    {
        if (minimumIntervals < 2) throw new ArgumentOutOfRangeException(nameof(minimumIntervals));
        var ordered = source.OrderBy(p => p.PositionMm).ThenBy(p => p.Boundary).ThenBy(p => p.Id).ToList();
        List<DetailingChainPoint> unique = new();
        foreach (DetailingChainPoint point in ordered)
            if (unique.Count == 0 || point.PositionMm - unique[unique.Count - 1].PositionMm > toleranceMm) unique.Add(point);
        List<DetailingChainPoint> selected = new();
        List<DetailingChainSpan> spans = new();
        int index = 0;
        while (index < unique.Count)
        {
            DetailingChainPoint first = unique[index];
            selected.Add(first);
            if (first.Boundary || index + 1 >= unique.Count) { index++; continue; }
            int last = index;
            double pitch = unique[index + 1].PositionMm - first.PositionMm;
            while (last + 1 < unique.Count)
            {
                DetailingChainPoint next = unique[last + 1];
                bool sameRun = !next.Boundary && next.RunKey == first.RunKey;
                bool samePitch = Math.Abs(next.PositionMm - unique[last].PositionMm - pitch) <= toleranceMm;
                if (!sameRun || !samePitch) break;
                last++;
            }
            if (last - index >= minimumIntervals)
            {
                spans.Add(new DetailingChainSpan(first, unique[last], last - index, pitch));
                index = last;
            }
            else index++;
        }
        return new DetailingChainPlan(selected, spans);
    }
}
