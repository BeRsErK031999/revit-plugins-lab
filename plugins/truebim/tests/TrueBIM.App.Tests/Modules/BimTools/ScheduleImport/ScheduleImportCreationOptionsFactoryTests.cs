using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleImportCreationOptionsFactoryTests
{
    [Fact]
    public void Create_RoutesImportToNewRevitSchedule()
    {
        ImportOptions options = ScheduleImportCreationOptionsFactory.Create(tableScale: 1.25);

        Assert.Equal(ScheduleImportMode.RevitSchedule, options.Mode);
        Assert.Equal(1.25, options.TableScale);
        Assert.False(options.DryRun);
    }
}
