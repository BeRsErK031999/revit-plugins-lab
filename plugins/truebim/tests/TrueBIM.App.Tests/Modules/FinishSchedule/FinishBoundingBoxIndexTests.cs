using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishBoundingBoxIndexTests
{
    [Fact]
    public void Query_ReturnsOnlyIntersectingElementsInDeterministicOrder()
    {
        FinishBoundingBoxIndex index = new(
        [
            Classified(3, new AxisAlignedBox3D(20, 0, 0, 21, 1, 1)),
            Classified(2, new AxisAlignedBox3D(5, 5, 0, 6, 6, 1)),
            Classified(1, new AxisAlignedBox3D(1, 1, 0, 2, 2, 1))
        ]);

        IReadOnlyList<FinishClassifiedElement> result = index.Query(
            new AxisAlignedBox3D(0, 0, 0, 10, 3, 2));

        Assert.Equal([1L], result.Select(element => element.Element.ElementId));
        Assert.Equal(3, index.IndexedElementCount);
    }

    [Fact]
    public void Constructor_TracksElementsWithoutBounds()
    {
        FinishBoundingBoxIndex index = new(
        [
            Classified(1, null),
            Classified(2, new AxisAlignedBox3D(0, 0, 0, 1, 1, 1))
        ]);

        Assert.Equal(1, index.ElementsWithoutBounds);
        Assert.Equal(1, index.IndexedElementCount);
    }

    private static FinishClassifiedElement Classified(long id, AxisAlignedBox3D? bounds)
    {
        return new FinishClassifiedElement(
            new FinishElementCandidateSnapshot(id, 100 + id, FinishPhysicalCategory.Wall, bounds),
            FinishPreviewCategory.Walls);
    }
}
