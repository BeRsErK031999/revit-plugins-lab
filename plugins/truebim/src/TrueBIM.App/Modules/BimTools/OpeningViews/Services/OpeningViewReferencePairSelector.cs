namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewReferencePairSelector
{
    public static bool TrySelect(
        IReadOnlyList<double> positions,
        out int minimumIndex,
        out int maximumIndex,
        double tolerance = 1e-6)
    {
        return TrySelect(
            positions,
            null,
            out minimumIndex,
            out maximumIndex,
            tolerance);
    }

    public static bool TrySelect(
        IReadOnlyList<double> positions,
        IReadOnlyList<double>? weights,
        out int minimumIndex,
        out int maximumIndex,
        double tolerance = 1e-6,
        double minimumRelativeWeight = 0.005)
    {
        minimumIndex = -1;
        maximumIndex = -1;
        if (positions is null
            || positions.Count < 2
            || weights is not null && weights.Count != positions.Count)
        {
            return false;
        }

        double minimumWeight = 0;
        if (weights is not null && weights.Count > 0)
        {
            double maximumWeight = weights.Max();
            minimumWeight = maximumWeight > 0
                ? maximumWeight * Math.Max(0, minimumRelativeWeight)
                : 0;
        }

        for (int index = 0; index < positions.Count; index++)
        {
            if (weights is not null && weights[index] < minimumWeight)
            {
                continue;
            }

            if (minimumIndex < 0 || positions[index] < positions[minimumIndex])
            {
                minimumIndex = index;
            }

            if (maximumIndex < 0 || positions[index] > positions[maximumIndex])
            {
                maximumIndex = index;
            }
        }

        if (minimumIndex >= 0
            && maximumIndex >= 0
            && positions[maximumIndex] - positions[minimumIndex] > Math.Max(0, tolerance))
        {
            return true;
        }

        minimumIndex = -1;
        maximumIndex = -1;
        return false;
    }
}
