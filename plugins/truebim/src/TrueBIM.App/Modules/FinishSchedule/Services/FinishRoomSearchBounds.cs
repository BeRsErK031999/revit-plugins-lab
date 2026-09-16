using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

/// <summary>Область поиска по этажу, независимая от заданной высоты помещения.</summary>
public sealed class FinishRoomSearchBounds
{
    private readonly IReadOnlyList<FinishRoomCandidateSnapshot> occupiedRooms;

    public FinishRoomSearchBounds(IEnumerable<FinishRoomCandidateSnapshot> rooms)
    {
        occupiedRooms = rooms.Where(room => room.Bounds is not null && room.HasLocation && room.Area > 1e-9).ToArray();
    }

    public AxisAlignedBox3D Create(FinishRoomCandidateSnapshot room, FinishPreviewCategory category)
    {
        AxisAlignedBox3D bounds = room.Bounds ?? throw new ArgumentException("Room bounds are required.", nameof(room));
        double tolerance = FinishCandidateSearchRules.HorizontalToleranceInternal;
        double upper = occupiedRooms.Where(other => other.LevelId != room.LevelId
                && other.Bounds!.MinZ > bounds.MinZ + tolerance && OverlapsInPlan(bounds, other.Bounds, tolerance))
            .Select(other => other.Bounds!.MinZ).DefaultIfEmpty(double.MaxValue).Min();
        // A room on an intermediate level in another wing must not cut off a high ceiling here.
        double lower = category == FinishPreviewCategory.Floors ? bounds.MinZ - 1.0 : bounds.MinZ - tolerance;
        double top = category == FinishPreviewCategory.Floors ? bounds.MinZ + 1.0 : upper - tolerance;
        return new AxisAlignedBox3D(bounds.MinX - tolerance, bounds.MinY - tolerance, lower,
            bounds.MaxX + tolerance, bounds.MaxY + tolerance, top);
    }

    private static bool OverlapsInPlan(AxisAlignedBox3D first, AxisAlignedBox3D second, double tolerance) =>
        Math.Min(first.MaxX, second.MaxX) - Math.Max(first.MinX, second.MinX) > tolerance
        && Math.Min(first.MaxY, second.MaxY) - Math.Max(first.MinY, second.MinY) > tolerance;
}
