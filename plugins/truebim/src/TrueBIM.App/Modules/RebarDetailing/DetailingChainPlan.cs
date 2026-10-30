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

public sealed class DetailingChainSection
{
    public IReadOnlyList<DetailingChainPoint> Points { get; }
    public bool UseEqualityFormula { get; }
    public int Intervals => Points.Count - 1;
    public double PitchMm => (Points[Points.Count - 1].PositionMm - Points[0].PositionMm) / Intervals;
    internal DetailingChainSection(IReadOnlyList<DetailingChainPoint> points, bool equality)
    { Points = points; UseEqualityFormula = equality; }
}

public sealed class DetailingChainPlan
{
    public IReadOnlyList<DetailingChainPoint> Points { get; }
    public IReadOnlyList<DetailingChainSection> Sections { get; }
    private DetailingChainPlan(IReadOnlyList<DetailingChainPoint> points, IReadOnlyList<DetailingChainSection> sections)
    { Points = points; Sections = sections; }

    // Keep every reference in a regular run: Revit's equality formula measures
    // the segments itself. A cover or shape transition is always a separate span.
    public static DetailingChainPlan Create(IEnumerable<DetailingChainPoint> source, bool equality = true, double toleranceMm = 0.1)
    {
        if (toleranceMm < 0) throw new ArgumentOutOfRangeException(nameof(toleranceMm));
        var ordered = source.OrderBy(p => p.PositionMm).ThenBy(p => p.Boundary).ThenBy(p => p.Id).ToList();
        List<DetailingChainPoint> unique = new();
        foreach (DetailingChainPoint point in ordered)
        {
            if (unique.Count == 0 || point.PositionMm - unique[unique.Count - 1].PositionMm > toleranceMm) { unique.Add(point); continue; }
            DetailingChainPoint previous = unique[unique.Count - 1];
            if (!previous.Boundary && !point.Boundary && previous.RunKey != point.RunKey)
                unique[unique.Count - 1] = new DetailingChainPoint(previous.Id, previous.PositionMm, previous.RunKey, true);
        }
        List<DetailingChainSection> sections = new();
        if (!equality)
        {
            if (unique.Count > 1) sections.Add(new DetailingChainSection(unique, false));
            return new DetailingChainPlan(unique, sections);
        }
        int index = 0;
        while (index + 1 < unique.Count)
        {
            int last = EqualEnd(index);
            bool formula = last - index >= 2;
            if (!formula)
            {
                last = index + 1;
                // Irregular intervals of one shape remain numeric. Stop before
                // an equality run, a concrete face, or a transition to another shape.
                while (SameRun(index, last) && last + 1 < unique.Count && SameRun(last, last + 1) && EqualEnd(last) - last < 2) last++;
            }
            sections.Add(new DetailingChainSection(unique.Skip(index).Take(last - index + 1).ToList(), formula));
            index = last;
        }
        return new DetailingChainPlan(unique, sections);

        bool SameRun(int a, int b) => !unique[a].Boundary && !unique[b].Boundary && unique[a].RunKey == unique[b].RunKey;
        int EqualEnd(int start)
        {
            if (start + 1 >= unique.Count || !SameRun(start, start + 1)) return start;
            double pitch = unique[start + 1].PositionMm - unique[start].PositionMm;
            int end = start + 1;
            while (end + 1 < unique.Count && SameRun(start, end + 1)
                && Math.Abs(unique[end + 1].PositionMm - unique[end].PositionMm - pitch) <= toleranceMm) end++;
            return end;
        }
    }
}
