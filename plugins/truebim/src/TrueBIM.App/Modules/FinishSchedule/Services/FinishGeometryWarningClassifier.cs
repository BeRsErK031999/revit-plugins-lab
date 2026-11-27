using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishGeometryWarningClassifier
{
    internal static bool NeedsStoredOwnershipGuidance(FinishSchedulePreviewResult preview)
    {
        var explainedByGeometry = preview.GeometryWarnings
            .Where(w => w.ElementId.HasValue && w.OwnershipFinding is not null and not FinishOwnershipFinding.EmptyParameter)
            .Select(w => w.ElementId).ToHashSet();
        return preview.GeometryWarnings.Any(w => w.Code == FinishGeometryWarningCode.StoredOwnershipUnresolved
            && (w.OwnershipFinding != FinishOwnershipFinding.EmptyParameter || !explainedByGeometry.Contains(w.ElementId)));
    }

    public static bool HasIncompleteScheduleValues(FinishSchedulePreviewResult preview)
    {
        if (preview is null)
        {
            throw new ArgumentNullException(nameof(preview));
        }

        return preview.GeometryWarnings.Any(AffectsScheduleValue);
    }

    public static bool AffectsScheduleValue(FinishGeometryWarning warning)
    {
        if (warning is null)
        {
            throw new ArgumentNullException(nameof(warning));
        }

        return warning.Code is FinishGeometryWarningCode.RoomNotFound
            or FinishGeometryWarningCode.RoomGeometryUnavailable
            or FinishGeometryWarningCode.WallFallbackUnresolved
            or FinishGeometryWarningCode.SlabGeometryUnsupported
            or FinishGeometryWarningCode.ProbeCreationFailed
            or FinishGeometryWarningCode.ProjectedAreaUnavailable
            or FinishGeometryWarningCode.FullElementAreaUnavailable
            or FinishGeometryWarningCode.UnassignedElement
            or FinishGeometryWarningCode.StoredOwnershipUnresolved;
    }
}
