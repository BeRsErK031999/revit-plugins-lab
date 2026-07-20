using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public static class FinishGeometryWarningClassifier
{
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
            or FinishGeometryWarningCode.ProjectedAreaUnavailable;
    }
}
