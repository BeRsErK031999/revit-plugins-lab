using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Modules.SheetNumbering.Rules;
using TrueBIM.App.Modules.SheetNumbering.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.SheetNumbering.Services;

public sealed class SheetPreviewOrderServiceTests
{
    private readonly SheetPreviewOrderService service = new();

    [Fact]
    public void MoveUp_SwapsSelectedItemWithPreviousItem()
    {
        SheetPreviewOrderChange<int> change = service.MoveUp([1, 2, 3], 1);

        Assert.True(change.Changed);
        Assert.Equal([2, 1, 3], change.Items);
        Assert.Equal(0, change.SelectedIndex);
    }

    [Fact]
    public void MoveDown_SwapsSelectedItemWithNextItem()
    {
        SheetPreviewOrderChange<int> change = service.MoveDown([1, 2, 3], 1);

        Assert.True(change.Changed);
        Assert.Equal([1, 3, 2], change.Items);
        Assert.Equal(2, change.SelectedIndex);
    }

    [Fact]
    public void MoveToPosition_UsesOneBasedTargetPosition()
    {
        SheetPreviewOrderChange<int> change = service.MoveToPosition([1, 2, 3, 4], 3, 2);

        Assert.True(change.Changed);
        Assert.Equal([1, 4, 2, 3], change.Items);
        Assert.Equal(1, change.SelectedIndex);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 4)]
    [InlineData(4, 1)]
    public void MoveToPosition_RejectsInvalidPositions(int selectedIndex, int targetPosition)
    {
        SheetPreviewOrderChange<int> change = service.MoveToPosition([1, 2, 3], selectedIndex, targetPosition);

        Assert.False(change.Changed);
        Assert.Equal([1, 2, 3], change.Items);
    }

    [Fact]
    public void PreviewOrderFollowsManualOrder()
    {
        SheetInfo[] sheets =
        [
            new(1, "A-01", "First", false),
            new(2, "A-02", "Second", false),
            new(3, "A-03", "Third", false)
        ];
        SheetPreviewOrderChange<SheetInfo> change = service.MoveToPosition(sheets, 2, 1);
        SheetNumberPreviewService previewService = new();

        IReadOnlyList<SheetNumberPreview> previews = previewService.GeneratePreviews(
            change.Items,
            new NumberingRules("S-", string.Empty, 1, 1, 2));

        Assert.Equal(3, previews[0].Sheet.ElementId);
        Assert.Equal("S-01", previews[0].PreviewNumber);
        Assert.Equal(1, previews[1].Sheet.ElementId);
        Assert.Equal("S-02", previews[1].PreviewNumber);
    }

    [Fact]
    public void ChangedResultCanInvalidatePreviewState()
    {
        bool isPreviewCurrent = true;
        SheetPreviewOrderChange<int> change = service.MoveDown([1, 2, 3], 0);

        if (change.Changed)
        {
            isPreviewCurrent = false;
        }

        Assert.False(isPreviewCurrent);
    }
}
