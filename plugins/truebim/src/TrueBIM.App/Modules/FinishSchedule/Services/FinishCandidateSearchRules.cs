using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishCandidateSearchRules
{
    public const double HorizontalToleranceInternal = 0.05;
    public const double VerticalSearchDistanceInternal = 6.56167979002625;

    public static AxisAlignedBox3D CreateSearchBounds(
        AxisAlignedBox3D roomBounds,
        FinishPreviewCategory category)
    {
        if (roomBounds is null)
        {
            throw new ArgumentNullException(nameof(roomBounds));
        }

        double maxZExpansion = category == FinishPreviewCategory.Ceilings
            ? VerticalSearchDistanceInternal
            : HorizontalToleranceInternal;
        return new AxisAlignedBox3D(
            roomBounds.MinX - HorizontalToleranceInternal,
            roomBounds.MinY - HorizontalToleranceInternal,
            roomBounds.MinZ - HorizontalToleranceInternal,
            roomBounds.MaxX + HorizontalToleranceInternal,
            roomBounds.MaxY + HorizontalToleranceInternal,
            roomBounds.MaxZ + maxZExpansion);
    }
}
