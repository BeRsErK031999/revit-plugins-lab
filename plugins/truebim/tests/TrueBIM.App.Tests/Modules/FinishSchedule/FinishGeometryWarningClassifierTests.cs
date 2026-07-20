using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishGeometryWarningClassifierTests
{
    [Theory]
    [InlineData(FinishGeometryWarningCode.BooleanIntersectionFailed)]
    [InlineData(FinishGeometryWarningCode.ElementGeometryUnavailable)]
    [InlineData(FinishGeometryWarningCode.ElementNotFound)]
    public void SpeculativeCandidateWarnings_AreDiagnostic(FinishGeometryWarningCode code)
    {
        Assert.False(FinishGeometryWarningClassifier.AffectsScheduleValue(Warning(code)));
    }

    [Theory]
    [InlineData(FinishGeometryWarningCode.RoomGeometryUnavailable)]
    [InlineData(FinishGeometryWarningCode.WallFallbackUnresolved)]
    [InlineData(FinishGeometryWarningCode.SlabGeometryUnsupported)]
    [InlineData(FinishGeometryWarningCode.ProjectedAreaUnavailable)]
    public void ConfirmedCompletenessWarnings_AreCritical(FinishGeometryWarningCode code)
    {
        Assert.True(FinishGeometryWarningClassifier.AffectsScheduleValue(Warning(code)));
    }

    private static FinishGeometryWarning Warning(FinishGeometryWarningCode code)
    {
        return new FinishGeometryWarning(code, "Warning", RoomId: 1, ElementId: 2);
    }
}
