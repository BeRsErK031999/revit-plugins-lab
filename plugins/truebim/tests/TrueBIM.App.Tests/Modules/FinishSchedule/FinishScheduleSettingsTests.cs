using TrueBIM.App.Modules.FinishSchedule.Models;
using Xunit;

namespace TrueBIM.App.Tests.Modules.FinishSchedule;

public sealed class FinishScheduleSettingsTests
{
    [Fact]
    public void CreateDefault_UsesSpecificationDefaultsWithoutInventingParameters()
    {
        FinishScheduleSettings settings = FinishScheduleSettings.CreateDefault();

        Assert.Equal(RoomIdentifierMode.Number, settings.RoomIdentifier.Mode);
        Assert.Equal(ReportScopeKind.EntireProject, settings.Scope.Kind);
        Assert.False(settings.WriteOwnership);
        Assert.True(settings.Walls.IsEnabled);
        Assert.True(settings.Floors.IsEnabled);
        Assert.True(settings.Ceilings.IsEnabled);
        Assert.Equal("Внутренняя отделка", settings.Walls.ClassificationValue);
        Assert.Equal("Пол", settings.Floors.ClassificationValue);
        Assert.Equal("Потолки", settings.Ceilings.ClassificationValue);
        Assert.Null(settings.DescriptionParameter);
        Assert.Null(settings.RoomListOutputParameter);
        Assert.Equal(FinishScheduleSettings.DefaultScheduleName, settings.ScheduleName);
    }
}
