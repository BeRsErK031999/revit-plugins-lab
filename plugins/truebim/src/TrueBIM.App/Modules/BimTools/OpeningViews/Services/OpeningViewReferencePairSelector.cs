namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public static class OpeningViewReferencePairSelector
{
    public static bool TrySelect(
        IReadOnlyList<double> positions,
        out int minimumIndex,
        out int maximumIndex,
        double tolerance = 1e-6)
    {
        minimumIndex = -1;
        maximumIndex = -1;
        if (positions is null || positions.Count < 2)
        {
            return false;
        }

        minimumIndex = 0;
        maximumIndex = 0;
        for (int index = 1; index < positions.Count; index++)
        {
            if (positions[index] < positions[minimumIndex])
            {
                minimumIndex = index;
            }

            if (positions[index] > positions[maximumIndex])
            {
                maximumIndex = index;
            }
        }

        if (positions[maximumIndex] - positions[minimumIndex] > Math.Max(0, tolerance))
        {
            return true;
        }

        minimumIndex = -1;
        maximumIndex = -1;
        return false;
    }
}
