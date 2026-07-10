using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewAnnotationLayoutTests
{
    [Fact]
    public void Create_UsesPaperOffsetsScaledIntoModelCoordinates()
    {
        OpeningViewProjectedBounds bounds = new(-2, 2, 0, 7);

        OpeningViewAnnotationLayout layout = OpeningViewAnnotationLayout.Create(bounds, viewScale: 50);

        Assert.Equal(-2, layout.HorizontalStart, 6);
        Assert.Equal(2, layout.HorizontalEnd, 6);
        Assert.Equal(-(4d * 50 / 304.8), layout.HorizontalPosition, 6);
        Assert.Equal(2 + (4d * 50 / 304.8), layout.VerticalPosition, 6);
        Assert.Equal(7 + (6d * 50 / 304.8), layout.TitleVertical, 6);
    }

    [Fact]
    public void Create_ReservesCropRoomAroundDimensionsAndTitle()
    {
        OpeningViewProjectedBounds bounds = new(-2, 2, 0, 7);

        OpeningViewAnnotationLayout layout = OpeningViewAnnotationLayout.Create(bounds, viewScale: 25);

        Assert.True(layout.RequiredMinHorizontal < bounds.MinHorizontal);
        Assert.True(layout.RequiredMaxHorizontal > layout.VerticalPosition);
        Assert.True(layout.RequiredMinVertical < layout.HorizontalPosition);
        Assert.True(layout.RequiredMaxVertical > layout.TitleVertical);
    }
}
