using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewReferenceNameMatcherTests
{
    [Theory]
    [InlineData("Проём левый", OpeningViewReferenceSide.Left, 200)]
    [InlineData("Правая граница проема", OpeningViewReferenceSide.Right, 200)]
    [InlineData("opening bottom", OpeningViewReferenceSide.Bottom, 200)]
    [InlineData("Верхняя грань", OpeningViewReferenceSide.Top, 100)]
    [InlineData("LEFT", OpeningViewReferenceSide.Left, 100)]
    public void Score_RecognizesRussianAndEnglishBoundaryNames(
        string name,
        OpeningViewReferenceSide side,
        int expected)
    {
        Assert.Equal(expected, OpeningViewReferenceNameMatcher.Score(name, side));
    }

    [Theory]
    [InlineData("Center (Left/Right)", OpeningViewReferenceSide.Left)]
    [InlineData("Center (Left/Right)", OpeningViewReferenceSide.Right)]
    [InlineData("Ось левая/правая", OpeningViewReferenceSide.Left)]
    [InlineData("Направление взгляда", OpeningViewReferenceSide.Right)]
    public void Score_RejectsCenterAmbiguousAndIncidentalNames(
        string name,
        OpeningViewReferenceSide side)
    {
        Assert.Equal(0, OpeningViewReferenceNameMatcher.Score(name, side));
    }
}
