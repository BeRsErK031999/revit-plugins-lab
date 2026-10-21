namespace TrueBIM.App.Modules.RebarSplit.Models;

public enum RebarSplitScope { Selection, VaryingSetsInProject }
public enum RebarSplitParameterAction { Cancel, Skip, SplitAnyway }

public sealed record RebarSplitOptions(RebarSplitScope Scope, bool PreserveAnnotations = true);

public sealed record RebarSplitParameterIssue(long SourceId, IReadOnlyList<string> Parameters);

public static class RebarSplitPolicy
{
    public const double LengthToleranceFeet = 0.1 / 304.8;

    public static bool HasDifferentLengths(IEnumerable<double> lengths)
    {
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        int count = 0;
        foreach (double length in lengths)
        {
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
                throw new ArgumentException("Не удалось определить фактическую длину стержня.", nameof(lengths));
            minimum = Math.Min(minimum, length);
            maximum = Math.Max(maximum, length);
            count++;
        }
        return count > 1 && maximum - minimum > LengthToleranceFeet;
    }
}
