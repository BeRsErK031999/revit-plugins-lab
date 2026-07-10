using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewBoundsResolverTests
{
    [Fact]
    public void Select_PrefersCompleteModelBoundsOverViewSpecificCutBounds()
    {
        OpeningViewBounds modelBounds = new(-2, -1, 0, 2, 1, 8);
        OpeningViewBounds viewSpecificBounds = new(-1, -1, 3, 1, 1, 4);

        OpeningViewBoundsResult? result = OpeningViewBoundsResolver.Select(modelBounds, viewSpecificBounds);

        Assert.NotNull(result);
        Assert.Same(modelBounds, result.Bounds);
        Assert.False(result.UsedViewSpecificFallback);
        Assert.Equal(0, result.Bounds.MinZ);
        Assert.Equal(8, result.Bounds.MaxZ);
    }

    [Fact]
    public void Select_UsesViewSpecificBoundsOnlyWhenModelBoundsAreMissing()
    {
        OpeningViewBounds viewSpecificBounds = new(-1, -1, 3, 1, 1, 4);

        OpeningViewBoundsResult? result = OpeningViewBoundsResolver.Select(null, viewSpecificBounds);

        Assert.NotNull(result);
        Assert.Same(viewSpecificBounds, result.Bounds);
        Assert.True(result.UsedViewSpecificFallback);
    }

    [Fact]
    public void Select_ReturnsNullWhenNoBoundsAreAvailable()
    {
        OpeningViewBoundsResult? result = OpeningViewBoundsResolver.Select(null, null);

        Assert.Null(result);
    }
}
