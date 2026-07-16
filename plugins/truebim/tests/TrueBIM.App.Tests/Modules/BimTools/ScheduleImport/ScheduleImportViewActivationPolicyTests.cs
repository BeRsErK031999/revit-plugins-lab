using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleImportViewActivationPolicyTests
{
    [Fact]
    public void ShouldOpenCreatedSchedule_ReturnsTrueForSuccessfulSchedule()
    {
        ScheduleImportCreationResult result = CreateResult(scheduleId: 42);

        Assert.True(ScheduleImportViewActivationPolicy.ShouldOpenCreatedSchedule(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void ShouldOpenCreatedSchedule_ReturnsFalseWithoutScheduleId(long? scheduleId)
    {
        ScheduleImportCreationResult result = CreateResult(scheduleId);

        Assert.False(ScheduleImportViewActivationPolicy.ShouldOpenCreatedSchedule(result));
    }

    [Fact]
    public void ShouldOpenCreatedSchedule_ReturnsFalseForFailedCreation()
    {
        ScheduleImportCreationResult result = CreateResult(42) with { Errors = ["failed"] };

        Assert.False(ScheduleImportViewActivationPolicy.ShouldOpenCreatedSchedule(result));
    }

    private static ScheduleImportCreationResult CreateResult(long? scheduleId)
    {
        return new ScheduleImportCreationResult(
            "TrueBIM_Импорт таблицы",
            scheduleId,
            false,
            10,
            5,
            Array.Empty<string>(),
            Array.Empty<string>());
    }
}
