using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;
using TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FamilyReplacement;

public sealed class FamilyReplacementSelectionCoordinatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void ReplacementRunsAfterCanvasSelectionAndControlsAreCleared(int count)
    {
        long[] sources = Enumerable.Range(1, count).Select(id => (long)id).ToArray();
        FakeSelection selection = new(sources);
        FamilyReplacementResult result = FamilyReplacementSelectionCoordinator.Execute(selection, () =>
        {
            Assert.Empty(selection.Selected);
            Assert.False(selection.ControlsVisible);
            Assert.Equal(count, sources.Length);
            return new FamilyReplacementResult(sources.Select(id => new FamilyReplacementItemResult(id, id + 100, true, "")).ToArray());
        }, exception => throw exception);

        Assert.Equal(count, result.Replaced);
        Assert.Equal(sources.Select(id => id + 100), selection.Selected);
    }

    [Fact]
    public void MixedResultsRestoreNewIdsSkippedSourcesAndUnrelatedSelection()
    {
        FakeSelection selection = new(new long[] { 1, 2, 9 });
        FamilyReplacementSelectionCoordinator.Execute(selection, () => new FamilyReplacementResult(new[]
        {
            new FamilyReplacementItemResult(1, 101, true, ""),
            new FamilyReplacementItemResult(2, null, false, "Real dependency prevents deletion"),
            new FamilyReplacementItemResult(3, 103, true, "Replaced from view scope")
        }), exception => throw exception);

        Assert.Equal(new long[] { 101, 2, 9 }, selection.Selected);
    }

    [Fact]
    public void FailedOperationRestoresOriginalSelectionWithoutReplacingException()
    {
        FakeSelection selection = new(new long[] { 1 });
        InvalidOperationException expected = new("Document transaction failed");
        Exception actual = Assert.Throws<InvalidOperationException>(() => FamilyReplacementSelectionCoordinator.Execute(
            selection, () => throw expected, exception => throw exception));

        Assert.Same(expected, actual);
        Assert.Equal(new long[] { 1 }, selection.Selected);
    }

    [Fact]
    public void ViewScopeDoesNotSelectEveryReplacedElementWhenSelectionWasEmpty()
    {
        FakeSelection selection = new(Array.Empty<long>());
        FamilyReplacementSelectionCoordinator.Execute(selection,
            () => new FamilyReplacementResult(new[] { new FamilyReplacementItemResult(1, 101, true, "") }), exception => throw exception);
        Assert.Empty(selection.Selected);
    }

    [Fact]
    public void SelectionRestoreFailureDoesNotHideSuccessfulReplacement()
    {
        FakeSelection selection = new(new long[] { 1 }) { FailRestoration = true };
        List<Exception> warnings = new();
        FamilyReplacementResult result = FamilyReplacementSelectionCoordinator.Execute(selection,
            () => new FamilyReplacementResult(new[] { new FamilyReplacementItemResult(1, 101, true, "") }), warnings.Add);

        Assert.Equal(1, result.Replaced);
        Assert.Single(warnings);
    }

    private sealed class FakeSelection : IFamilyReplacementSelectionContext
    {
        private int writes;

        public FakeSelection(IReadOnlyList<long> ids) => Selected = ids;
        public IReadOnlyList<long> Selected { get; private set; }
        public bool ControlsVisible { get; private set; } = true;
        public bool FailRestoration { get; set; }

        public IReadOnlyList<long> GetSelectedIds() => Selected;
        public void SetSelectedIds(IReadOnlyList<long> ids)
        {
            if (++writes > 1 && FailRestoration) throw new InvalidOperationException("View unavailable");
            Selected = ids;
        }
        public void RefreshView() => ControlsVisible = Selected.Count > 0;
        public bool ElementExists(long id) => true;
    }
}
