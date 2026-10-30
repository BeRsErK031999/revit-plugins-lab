using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingChainPlanTests
{
    [Fact]
    public void FieldRetainsAllReferencesAndSeparatesCoverShapeTransitionAndIrregularTies()
    {
        var points = new[] { new DetailingChainPoint(0, 0, "concrete", true) }
            .Concat(Enumerable.Range(0, 13).Select(i => new DetailingChainPoint(i + 1, 50 + i * 200, "000")))
            .Concat(new[] { new DetailingChainPoint(20, 2650, "X1"), new DetailingChainPoint(21, 2780, "X1"),
                new DetailingChainPoint(22, 2900, "X1"), new DetailingChainPoint(30, 2950, "concrete", true) }).ToList();
        var plan = DetailingChainPlan.Create(points);
        Assert.Equal(points.Count, plan.Points.Count);
        Assert.Equal(5, plan.Sections.Count);
        var formula = Assert.Single(plan.Sections, s => s.UseEqualityFormula);
        Assert.Equal(13, formula.Points.Count);
        Assert.Equal(12, formula.Intervals);
        Assert.Equal(200, formula.PitchMm);
        Assert.Equal(new[] { 2450.0, 2650 }, plan.Sections[2].Points.Select(p => p.PositionMm));
        Assert.Equal(new[] { 2650.0, 2780, 2900 }, plan.Sections[3].Points.Select(p => p.PositionMm));
        Assert.Equal(points.Count - 1, plan.Sections.Sum(s => s.Intervals));
    }

    [Fact]
    public void TwoRegularShapesWithSamePitchStillHaveASeparateBridge()
    {
        var plan = Make((0, "000"), (200, "000"), (400, "000"), (600, "X1"), (800, "X1"), (1000, "X1"));
        Assert.Equal(new[] { true, false, true }, plan.Sections.Select(s => s.UseEqualityFormula));
        Assert.Equal(new[] { 400.0, 600 }, plan.Sections[1].Points.Select(p => p.PositionMm));
        Assert.Equal(5, plan.Sections.Sum(s => s.Intervals));
    }

    [Fact]
    public void PitchChangeStartsNewNativeDimensionAtSharedReference()
    {
        var plan = Make((0, "000"), (200, "000"), (400, "000"), (550, "000"), (700, "000"), (860, "000"));
        Assert.Equal(new[] { true, true, false }, plan.Sections.Select(s => s.UseEqualityFormula));
        Assert.Equal(200, plan.Sections[0].PitchMm);
        Assert.Equal(150, plan.Sections[1].PitchMm);
        Assert.Equal(plan.Sections[0].Points.Last().Id, plan.Sections[1].Points.First().Id);
        Assert.Equal(5, plan.Sections.Sum(s => s.Intervals));
    }

    [Fact]
    public void CoincidentRowsAreDeduplicatedWithoutLosingAnyIntervals()
    {
        var plan = Make((400.03, "000"), (600, "000"), (0, "000"), (200, "000"), (400, "000"));
        Assert.Equal(4, plan.Points.Count);
        Assert.True(Assert.Single(plan.Sections).UseEqualityFormula);
        Assert.Equal(3, plan.Sections[0].Intervals);
    }

    [Fact]
    public void CoincidentDifferentShapesInterruptTheEqualityFormula()
    {
        var plan = Make((0, "000"), (200, "000"), (200, "X1"), (400, "000"), (600, "000"));
        Assert.DoesNotContain(plan.Sections, s => s.UseEqualityFormula);
        Assert.Equal(3, plan.Sections.Sum(s => s.Intervals));
    }

    [Fact]
    public void IrregularSequenceRemainsOneNumericChain()
    {
        var plan = Make((0, "X1"), (120, "X1"), (250, "X1"), (370, "X1"), (500, "X1"));
        Assert.False(Assert.Single(plan.Sections).UseEqualityFormula);
        Assert.Equal(5, plan.Sections[0].Points.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void FewerThanTwoReferencesCannotCreateADimension(int count)
    {
        var plan = DetailingChainPlan.Create(Enumerable.Range(0, count).Select(i => new DetailingChainPoint(i, i * 200, "000")));
        Assert.Empty(plan.Sections);
    }

    [Fact]
    public void ConcreteAndCrossSectionChainsKeepNumericSegments()
    {
        var plan = DetailingChainPlan.Create(Enumerable.Range(0, 5).Select(i => new DetailingChainPoint(i, i * 200, "000")), false);
        Assert.False(Assert.Single(plan.Sections).UseEqualityFormula);
        Assert.Equal(5, plan.Sections[0].Points.Count);
    }

    private static DetailingChainPlan Make(params (double Position, string Shape)[] values) =>
        DetailingChainPlan.Create(values.Select((p, i) => new DetailingChainPoint(i, p.Position, p.Shape)));
}
