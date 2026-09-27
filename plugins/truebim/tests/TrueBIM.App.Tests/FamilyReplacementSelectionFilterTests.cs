using TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;
using Xunit;

namespace TrueBIM.App.Tests;

public sealed class FamilyReplacementSelectionFilterTests
{
    [Fact]
    public void SourceSearchExcludesPreviouslySelectedHiddenFamilies()
    {
        FamilyReplacementCandidateRow detector = Row(1, 10, "Датчик", "Пожарная сигнализация", "A1");
        FamilyReplacementCandidateRow siren = Row(2, 20, "Оповещатель", "Пожарная сигнализация", "A2");

        IReadOnlyList<long> selected = FamilyReplacementSelectionFilter.VisibleRows(
            new[] { detector, siren }, new HashSet<long> { 10, 20 }, "датчик", string.Empty)
            .Where(row => row.IsSelected).Select(row => row.Item.Id).ToList();

        Assert.Equal(new long[] { 1 }, selected);
        Assert.True(siren.IsSelected);
    }

    [Fact]
    public void InstanceSearchAndTypeFilterBothLimitOperands()
    {
        FamilyReplacementCandidateRow first = Row(1, 10, "Датчик", "Пожарная сигнализация", "A1");
        FamilyReplacementCandidateRow second = Row(2, 10, "Датчик", "Пожарная сигнализация", "A2");
        FamilyReplacementCandidateRow otherType = Row(3, 20, "Датчик", "Пожарная сигнализация", "A2");

        IReadOnlyList<long> visible = FamilyReplacementSelectionFilter.VisibleRows(
            new[] { first, second, otherType }, new HashSet<long> { 10 }, "пожарная", "A2")
            .Select(row => row.Item.Id).ToList();

        Assert.Equal(new long[] { 2 }, visible);
    }

    [Fact]
    public void EmptySearchPreservesExplicitlyClearedInstanceSelection()
    {
        FamilyReplacementCandidateRow first = Row(1, 10, "Датчик", "Пожарная сигнализация", "A1");
        FamilyReplacementCandidateRow second = Row(2, 10, "Датчик", "Пожарная сигнализация", "A2");
        second.IsSelected = false;

        IReadOnlyList<long> selected = FamilyReplacementSelectionFilter.VisibleRows(
            new[] { first, second }, new HashSet<long> { 10 }, "", "")
            .Where(row => row.IsSelected).Select(row => row.Item.Id).ToList();

        Assert.Equal(new long[] { 1 }, selected);
    }

    private static FamilyReplacementCandidateRow Row(long id, long typeId, string family, string category, string mark)
    {
        return new FamilyReplacementCandidateRow(new FamilyReplacementCandidate(
            id, typeId, 100, category, family, "Тип 1", "Уровень 1", mark, true));
    }
}
