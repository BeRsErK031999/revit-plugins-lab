using TrueBIM.App.Modules.SlabReinforcement;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class OpeningLayoutTests
{
    private static List<OpeningBox> Group(params OpeningBox[] holes) =>
        OpeningLayout.Groups(holes, (i, j) => holes[i].Gap(holes[j]));

    [Fact]
    public void GroupsNearbyHolesBeforeCheckingEitherDimension()
    {
        Assert.Equal(new OpeningBox(0, 400, 0, 1210), Assert.Single(Group(
            new(0, 400, 0, 380), new(0, 400, 460, 1210))));
        Assert.Equal(new OpeningBox(0, 480, 0, 200), Assert.Single(Group(
            new(0, 200, 0, 200), new(280, 480, 0, 200))));
        Assert.Empty(Group(new OpeningBox(0, 300, 0, 300)));
        Assert.Empty(Group(new(0, 200, 0, 200), new(350, 550, 0, 200)));
    }

    [Fact]
    public void DistanceBetweenActualContoursControlsGrouping()
    {
        OpeningBox[] holes = { new(0, 400, 0, 400), new(410, 810, 0, 400) };
        Assert.Equal(2, OpeningLayout.Groups(holes, (_, _) => 200).Count);
    }

    [Fact]
    public void UsesLongestPairInPierAndOutermostEdgeAtOffsetOf100()
    {
        var groups = Group(new(60, 300, 0, 260), new(0, 300, 330, 1040), new(60, 300, 1110, 1330),
            new(500, 870, 100, 500), new(500, 870, 580, 1460), new(500, 760, 1530, 1790));
        Assert.Equal(new[] { new OpeningBox(0, 300, 0, 1330), new OpeningBox(500, 870, 100, 1790) }, groups);
        var vertical = OpeningLayout.Rows(groups, true, 50, 100, 350);
        Assert.Equal(6, vertical.Count);
        Assert.Contains(new OpeningRow(350, 100, 1790), vertical);
        Assert.Contains(new OpeningRow(450, 100, 1790), vertical);
        var horizontal = OpeningLayout.Rows(groups, false, 50, 100, 350);
        Assert.Contains(new OpeningRow(-50, 0, 870), horizontal);
        Assert.Contains(new OpeningRow(-150, 0, 870), horizontal);
        Assert.Contains(new OpeningRow(1380, 0, 300), horizontal);
        Assert.Contains(new OpeningRow(1840, 500, 870), horizontal);
    }

    [Theory]
    [InlineData(150, true)]
    [InlineData(151, false)]
    public void Exactly150SharesAnExteriorPair(double offset, bool common)
    {
        var groups = Group(new(0, 400, 0, 500), new(600, 1000, offset, 500 + offset));
        var rows = OpeningLayout.Rows(groups, false, 50, 100, 350);
        Assert.Equal(common, rows.Contains(new OpeningRow(-50, 0, 1000)));
    }

    [Theory]
    [InlineData(10, 350)]
    [InlineData(12, 400)]
    public void OpeningLengthUsesAlphaOne(double diameter, double length) =>
        Assert.Equal(length, SlabLayoutRules.OpeningAnchor(diameter, 435, 1.4));
}
