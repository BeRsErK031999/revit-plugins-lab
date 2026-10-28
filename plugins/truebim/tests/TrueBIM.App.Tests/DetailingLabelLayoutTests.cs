using TrueBIM.App.Modules.RebarDetailing;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class DetailingLabelLayoutTests
{
    [Fact]
    public void NearbySectionLabelsSeparateLocallyWithoutPushingTheWholeStackAboveTheWall()
    {
        var requests = Enumerable.Range(0, 4).Select(index => new DetailingLabelRequest {
            Id = index, X = 3, Y = 28 + index, Width = 18, Height = 6,
            Host = new DetailingRect(0, 0, 6, 60), PreferredSide = DetailingLabelSide.Right, OnlyPreferredSide = true
        });
        var labels = DetailingLabelLayout.Place(requests, new[] { new DetailingRect(14, 32, 34, 42) });
        Assert.All(labels, label => { Assert.False(label.Crowded); Assert.InRange(label.Bounds.CenterY, 5, 65); });
        for (int index = 0; index < labels.Count; index++)
            Assert.All(labels.Skip(index + 1), other => Assert.False(labels[index].Bounds.Intersects(other.Bounds, 1.9)));
    }

    [Fact]
    public void CornerLabelsUseTheLocalLegAndDoNotUseTheWholeViewExtentAsTheirAnchor()
    {
        var labels = DetailingLabelLayout.Place(new[] {
            new DetailingLabelRequest { Id=1, X=40, Y=35, Width=18, Height=6, Host=new DetailingRect(36,0,44,50), PreferredSide=DetailingLabelSide.Left },
            new DetailingLabelRequest { Id=2, X=16, Y=4, Width=18, Height=6, Host=new DetailingRect(0,0,44,8), PreferredSide=DetailingLabelSide.Above }
        }, Array.Empty<DetailingRect>());
        Assert.Equal(DetailingLabelSide.Left, labels.Single(p => p.Request.Id == 1).Side);
        Assert.Equal(DetailingLabelSide.Above, labels.Single(p => p.Request.Id == 2).Side);
        Assert.All(labels, label => Assert.False(label.Crowded));
        Assert.False(labels[0].Bounds.Intersects(labels[1].Bounds));
    }

    [Fact]
    public void SeveralCornerLabelsCanUseRowsAboveTheHorizontalLeg()
    {
        var requests = Enumerable.Range(0, 5).Select(index => new DetailingLabelRequest {
            Id = index, X = 15 + index * 5, Y = 4, Width = 18, Height = 7,
            Host = new DetailingRect(0, 0, 44, 8), PreferredSide = DetailingLabelSide.Above, OnlyPreferredSide = true
        });
        var labels = DetailingLabelLayout.Place(requests, new[] { new DetailingRect(36, 0, 44, 50) }, 5, 2);
        Assert.All(labels, label => { Assert.Equal(DetailingLabelSide.Above, label.Side); Assert.False(label.Crowded); });
        Assert.True(labels.Select(label => label.Bounds.Bottom).Distinct().Count() > 1);
        Assert.True(labels.Max(label => label.Bounds.Right) - labels.Min(label => label.Bounds.Left) < 90);
    }

    [Fact]
    public void SideLabelsStayInsideTheCornerVoidAndAvoidDimensionText()
    {
        var requests = Enumerable.Range(0, 4).Select(index => new DetailingLabelRequest {
            Id = index, X = 6, Y = 28 + index * 9, Width = 17, Height = 7,
            Host = new DetailingRect(0, 0, 8, 60), PreferredSide = DetailingLabelSide.Right,
            OnlyPreferredSide = true, MinX = 10, MaxX = 39, MinY = 10, MaxY = 65
        });
        var text = new[] { new DetailingRect(40, 5, 44, 55), new DetailingRect(10, -7, 38, -2) };
        var labels = DetailingLabelLayout.Place(requests, text, 5, 2, text);
        Assert.All(labels, label => {
            Assert.False(label.Crowded);
            Assert.InRange(label.Bounds.Left, 10, 22);
            Assert.InRange(label.Bounds.Right, 27, 39);
            Assert.InRange(label.Bounds.Bottom, 10, 58);
            Assert.InRange(label.Bounds.Top, 17, 65);
            Assert.All(text, obstacle => Assert.False(obstacle.Intersects(label.Bounds, 1.9)));
        });
    }

    [Fact]
    public void DenseCornerLabelsExpandBeyondFiveRowsInsteadOfOverlapping()
    {
        var requests = Enumerable.Range(0, 7).Select(index => new DetailingLabelRequest {
            Id=index, X=12, Y=4, Width=22, Height=7, MinX=0, MaxX=24, MinY=11,
            Host=new DetailingRect(0,0,24,8), PreferredSide=DetailingLabelSide.Above, OnlyPreferredSide=true
        });
        var labels = DetailingLabelLayout.Place(requests, Array.Empty<DetailingRect>(), 5, 2);
        Assert.All(labels, label => Assert.False(label.Crowded));
        for (int index=0; index<labels.Count; index++)
            Assert.All(labels.Skip(index+1), other => Assert.False(labels[index].Bounds.Intersects(other.Bounds, 1.9)));
    }

    [Fact]
    public void PreferredHeadHeightDoesNotReplaceTheActualLeaderTarget()
    {
        var request = new DetailingLabelRequest {
            Id=1, X=3, Y=4, PreferredY=-6, Width=17, Height=7,
            Host=new DetailingRect(0,0,8,30), PreferredSide=DetailingLabelSide.Left, OnlyPreferredSide=true
        };
        var label = Assert.Single(DetailingLabelLayout.Place(new[] { request }, Array.Empty<DetailingRect>(), 5, 2));
        Assert.Equal(-6, label.Bounds.CenterY);
        Assert.Equal(label.Bounds.CenterY, label.EndY);
        Assert.Equal(4, label.Request.Y);
    }
}
