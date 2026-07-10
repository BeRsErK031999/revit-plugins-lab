using TrueBIM.App.Modules.BimTools.OpeningViews.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.OpeningViews;

public sealed class OpeningViewReferencePairSelectorTests
{
    [Fact]
    public void TrySelect_ReturnsExtremeCandidateIndices()
    {
        bool selected = OpeningViewReferencePairSelector.TrySelect(
            [4.2, -1.5, 2.0, 8.75],
            out int minimumIndex,
            out int maximumIndex);

        Assert.True(selected);
        Assert.Equal(1, minimumIndex);
        Assert.Equal(3, maximumIndex);
    }

    [Fact]
    public void TrySelect_RejectsCoincidentFaces()
    {
        bool selected = OpeningViewReferencePairSelector.TrySelect(
            [2.0, 2.0 + 1e-8],
            out int minimumIndex,
            out int maximumIndex);

        Assert.False(selected);
        Assert.Equal(-1, minimumIndex);
        Assert.Equal(-1, maximumIndex);
    }
}
