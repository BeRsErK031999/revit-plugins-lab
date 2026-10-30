using TrueBIM.App.Modules.RebarSplit.Models;
using TrueBIM.App.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class RebarSplitPolicyTests
{
    [Fact]
    public void EqualAndSingleLengthsAreNotVariable()
    {
        Assert.False(RebarSplitPolicy.HasDifferentLengths(Array.Empty<double>()));
        Assert.False(RebarSplitPolicy.HasDifferentLengths(new[] { 3.0 }));
        Assert.False(RebarSplitPolicy.HasDifferentLengths(new[] { 3.0, 3.0, 3.0 }));
    }

    [Fact]
    public void UsesExtremesOfAllBarsAndPointOneMillimeterTolerance()
    {
        double tolerance = RebarSplitPolicy.LengthToleranceFeet;
        Assert.False(RebarSplitPolicy.HasDifferentLengths(new[] { 3.0, 3.0 + tolerance / 2 }));
        Assert.True(RebarSplitPolicy.HasDifferentLengths(new[] { 3.0, 3.0 + tolerance * 2, 3.0 }));
        Assert.True(RebarSplitPolicy.HasDifferentLengths(new[] { 3.0, 3.0 - tolerance * 0.75, 3.0 + tolerance * 0.75 }));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidLengthDoesNotSilentlySkipASet(double invalid)
    {
        Assert.Throws<ArgumentException>(() => RebarSplitPolicy.HasDifferentLengths(new[] { 3.0, invalid }));
    }

    [Fact]
    public void ButtonIsInKrDisciplineAndOffersParameterTransferAndAnnotations()
    {
        TrueBimRibbonButtonDefinition menu = Assert.Single(TrueBimRibbon.Buttons, item => item.Name == "TrueBIM_DisciplinesMenu");
        TrueBimRibbonPulldownItemDefinition button = Assert.Single(menu.Items, item => item.Name == "TrueBIM_RebarSplit");
        Assert.Equal("КР · Разбить арматуру", button.Text);
        Assert.Equal(TrueBimIcon.RebarSplit, button.Icon);
        Assert.EndsWith("RebarSplitCommand", button.CommandClassName);
        Assert.EndsWith("RebarSplitCommandAvailability", button.AvailabilityClassName);
        Assert.Contains("аннотаций", button.LongDescription);
    }
}
