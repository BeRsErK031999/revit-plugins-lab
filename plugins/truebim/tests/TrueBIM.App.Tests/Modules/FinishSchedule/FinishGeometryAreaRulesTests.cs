using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishGeometryAreaRulesTests
{
    [Fact]
    public void SelectHorizontalProjectedArea_DoesNotDoubleCountTopAndBottomFaces()
    {
        double? result = FinishGeometryAreaRules.SelectHorizontalProjectedArea(
        [
            new FinishFaceMeasure(25, 0, 0, 1),
            new FinishFaceMeasure(25, 0, 0, -1),
            new FinishFaceMeasure(4, 1, 0, 0)
        ]);

        Assert.Equal(25, result);
    }

    [Fact]
    public void SelectHorizontalProjectedArea_RejectsSlopedFaces()
    {
        double? result = FinishGeometryAreaRules.SelectHorizontalProjectedArea(
        [
            new FinishFaceMeasure(12, 0.2, 0, 0.98),
            new FinishFaceMeasure(8, 1, 0, 0)
        ]);

        Assert.Null(result);
    }

    [Fact]
    public void SelectParallelFaceArea_UsesOnlyFacesParallelToRoomBoundary()
    {
        double? result = FinishGeometryAreaRules.SelectParallelFaceArea(
        [
            new FinishFaceMeasure(18, -1, 0, 0),
            new FinishFaceMeasure(18, 1, 0, 0),
            new FinishFaceMeasure(40, 0, 1, 0)
        ],
            referenceX: 1,
            referenceY: 0,
            referenceZ: 0);

        Assert.Equal(18, result);
    }
}
