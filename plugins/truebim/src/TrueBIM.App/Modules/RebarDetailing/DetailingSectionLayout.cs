namespace TrueBIM.App.Modules.RebarDetailing;

public static class DetailingSectionLayout
{
    public static int LowerBreakKind(bool concreteLeft, bool concreteRight) => concreteLeft && concreteRight ? 3 : concreteLeft || concreteRight ? 2 : 1;

    public static DetailingVariant VariantForBends(IEnumerable<double> offsetsMm, double toleranceMm = 50)
    {
        bool left = false, right = false;
        foreach (double offset in offsetsMm)
        {
            left |= offset < -toleranceMm;
            right |= offset > toleranceMm;
        }
        return left && right ? DetailingVariant.BentOppositeSides : left || right ? DetailingVariant.BentSameSide : DetailingVariant.Straight;
    }

    public static bool HasIrregularPitch(IEnumerable<double> positionsMm, double toleranceMm = 0.1)
    {
        var ordered = positionsMm.OrderBy(x => x).ToList();
        var unique = new List<double>();
        foreach (double position in ordered)
            if (unique.Count == 0 || position - unique[unique.Count - 1] > toleranceMm) unique.Add(position);
        if (unique.Count < 3) return false;
        double pitch = unique[1] - unique[0];
        return Enumerable.Range(2, unique.Count - 2).Any(i => Math.Abs(unique[i] - unique[i - 1] - pitch) > toleranceMm);
    }
}
