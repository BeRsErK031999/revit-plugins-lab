using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingChainPlanTests
{
    [Fact]
    public void SectionCombinesTheFieldAndTwoTiesWithoutTreatingTheFirstTieAsAnotherFieldBar()
    {
        var points = new[] { new DetailingChainPoint(0, 0, "concrete", true) }
            .Concat(Enumerable.Range(0, 13).Select(index => new DetailingChainPoint(index + 1, 50 + index * 200, "field")))
            .Concat(new[] { new DetailingChainPoint(20, 2650, "tie"), new DetailingChainPoint(21, 2830, "tie"), new DetailingChainPoint(30, 2880, "concrete", true) });
        var plan = DetailingChainPlan.Create(points);
        Assert.Equal(new[] { 0.0, 50, 2450, 2650, 2830, 2880 }, plan.Points.Select(p => p.PositionMm));
        var span = Assert.Single(plan.EqualSpans);
        Assert.Equal(12, span.Intervals);
        Assert.Equal(200, span.PitchMm);
    }

    [Fact]
    public void CornerShowsIndividualShortIntervalsAndDeduplicatesCoincidentAxesFromTwoSets()
    {
        double[] distances = { 0, 200, 400, 600, 705, 795, 850 };
        var points = distances.Select((value, index) => new DetailingChainPoint(index, value, index < 4 ? "green" : "orange", index == 6))
            .Concat(new[] { new DetailingChainPoint(100, 400.03, "green") });
        var plan = DetailingChainPlan.Create(points.Reverse());
        Assert.Equal(distances, plan.Points.Select(p => p.PositionMm));
        Assert.Empty(plan.EqualSpans);
    }
}
