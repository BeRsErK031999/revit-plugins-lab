using TrueBIM.App.Modules.FinishSchedule.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleLaunchStateTests
{
    [Fact]
    public void Create_InvalidConfigurationNeverEnablesGeneration()
    {
        FinishScheduleValidationResult validation = new(
        [
            new FinishScheduleValidationIssue("field.missing", "field", "Выберите параметр.")
        ]);

        FinishScheduleLaunchState state = FinishScheduleLaunchState.Create(
            validation,
            workflowAvailable: true);

        Assert.False(state.IsConfigurationValid);
        Assert.False(state.CanGenerate);
        Assert.Contains("ошиб", state.StatusText, StringComparison.CurrentCultureIgnoreCase);
    }

    [Fact]
    public void Create_ValidConfigurationStillRequiresAvailableWorkflow()
    {
        FinishScheduleValidationResult validation = new([]);

        FinishScheduleLaunchState pending = FinishScheduleLaunchState.Create(
            validation,
            workflowAvailable: false);
        FinishScheduleLaunchState ready = FinishScheduleLaunchState.Create(
            validation,
            workflowAvailable: true);

        Assert.True(pending.IsConfigurationValid);
        Assert.False(pending.CanGenerate);
        Assert.True(ready.CanGenerate);
    }
}
