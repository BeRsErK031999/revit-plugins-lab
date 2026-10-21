using TrueBIM.App.Modules.RebarFill.Models;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class RebarFillSelectionTests
{
    [Fact]
    public void FilterWritesOnlyStraightBarsEvenWhenClampsAreChecked()
    {
        RebarFillSelectionItem[] items = { new(1, 10, "Прямой"), new(2, 20, "Хомут"), new(3, 10, "Прямой") };
        Assert.Equal(new long[] { 1, 3 }, RebarFillSelection.GetSelectedIds(items, 10));
        Assert.Equal(new long[] { 2 }, RebarFillSelection.GetSelectedIds(items, 20));
    }

    [Fact]
    public void UncheckedBarIsExcludedFromBothFilteredAndUnfilteredScope()
    {
        RebarFillSelectionItem[] items = { new(1, 10, "Прямой"), new(2, 10, "Прямой") { IsSelected = false } };
        Assert.Equal(new long[] { 1 }, RebarFillSelection.GetSelectedIds(items, 10));
        Assert.Equal(new long[] { 1 }, RebarFillSelection.GetSelectedIds(items, null));
    }

    [Fact]
    public void SwitchingFiltersDoesNotLoseCheckboxState()
    {
        RebarFillSelectionItem[] items = { new(1, 10, "Прямой") { IsSelected = false }, new(2, 20, "Хомут") };
        Assert.Empty(RebarFillSelection.GetSelectedIds(items, 10));
        Assert.Equal(new long[] { 2 }, RebarFillSelection.GetSelectedIds(items, 20));
        Assert.Empty(RebarFillSelection.GetSelectedIds(items, 10));
    }

    [Fact]
    public void TypeIdentitySeparatesEqualNames()
    {
        RebarFillSelectionItem[] items = { new(1, 10, "Стержень"), new(2, 20, "Стержень") };
        Assert.Equal(new long[] { 2 }, RebarFillSelection.GetSelectedIds(items, 20));
    }

    [Fact]
    public void NoSelectionProducesNoTargets()
    {
        Assert.Empty(RebarFillSelection.GetSelectedIds(new[] { new RebarFillSelectionItem(1, 10, "Прямой") { IsSelected = false } }, null));
        Assert.Empty(RebarFillSelection.GetSelectedIds(new[] { new RebarFillSelectionItem(1, 10, "Прямой") }, 20));
    }

    [Fact]
    public void DuplicateIdsAreNotWrittenTwice()
    {
        Assert.Equal(new long[] { 1 }, RebarFillSelection.GetSelectedIds(new[] { new RebarFillSelectionItem(1, 10, "Прямой"), new RebarFillSelectionItem(1, 10, "Прямой") }, null));
    }
}
