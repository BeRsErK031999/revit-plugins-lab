using TrueBIM.App.Modules.FinishSchedule.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleWriteResultTests
{
    [Fact]
    public void CanRetryWithSimplifiedHeader_WhenCustomHeaderFormattingFailed()
    {
        FinishScheduleWriteResult result = new(
            FinishScheduleWriteStatus.Failed,
            0,
            0,
            0,
            [],
            "Header failed.",
            FailureKind: FinishScheduleWriteFailureKind.HeaderFormatting);

        Assert.True(result.CanRetryWithSimplifiedHeader);
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData(FinishScheduleWriteStatus.Applied, FinishScheduleWriteFailureKind.HeaderFormatting)]
    [InlineData(FinishScheduleWriteStatus.Failed, FinishScheduleWriteFailureKind.None)]
    public void CanRetryWithSimplifiedHeader_RejectsOtherResults(
        FinishScheduleWriteStatus status,
        FinishScheduleWriteFailureKind failureKind)
    {
        FinishScheduleWriteResult result = new(
            status,
            0,
            0,
            0,
            [],
            "Result.",
            FailureKind: failureKind);

        Assert.False(result.CanRetryWithSimplifiedHeader);
    }
}
