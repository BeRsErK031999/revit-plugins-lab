using TrueBIM.App.Modules.Print.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.Print.Services;

public sealed class PrintSheetSelectionRangeTests
{
    [Fact]
    public void Resolve_ReturnsInclusiveForwardRangeWithAnchorState()
    {
        PrintSheetSelectionRange? result = PrintSheetSelectionRange.Resolve(
            itemCount: 8,
            anchorIndex: 2,
            targetIndex: 5,
            anchorIsSelected: true);

        Assert.NotNull(result);
        Assert.Equal(2, result.StartIndex);
        Assert.Equal(5, result.EndIndex);
        Assert.Equal(4, result.Count);
        Assert.True(result.IsSelected);
    }

    [Fact]
    public void Resolve_NormalizesReverseRangeAndPreservesUncheckedAnchor()
    {
        PrintSheetSelectionRange? result = PrintSheetSelectionRange.Resolve(
            itemCount: 8,
            anchorIndex: 6,
            targetIndex: 1,
            anchorIsSelected: false);

        Assert.NotNull(result);
        Assert.Equal(1, result.StartIndex);
        Assert.Equal(6, result.EndIndex);
        Assert.Equal(6, result.Count);
        Assert.False(result.IsSelected);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, -1, 1)]
    [InlineData(3, 1, -1)]
    [InlineData(3, 3, 1)]
    [InlineData(3, 1, 3)]
    public void Resolve_ReturnsNullForMissingOrOutOfRangeItems(
        int itemCount,
        int anchorIndex,
        int targetIndex)
    {
        PrintSheetSelectionRange? result = PrintSheetSelectionRange.Resolve(
            itemCount,
            anchorIndex,
            targetIndex,
            anchorIsSelected: true);

        Assert.Null(result);
    }
}
