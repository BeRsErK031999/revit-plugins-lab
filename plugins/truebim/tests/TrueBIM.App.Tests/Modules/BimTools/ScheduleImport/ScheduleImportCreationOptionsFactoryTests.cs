using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.ScheduleImport;

public sealed class ScheduleImportCreationOptionsFactoryTests
{
    [Fact]
    public void Create_RoutesVisualImportToDraftingView()
    {
        ImportOptions options = ScheduleImportCreationOptionsFactory.Create(
            activeViewId: 42,
            createNewViewIfNeeded: true,
            tableScale: 1.25);

        Assert.Equal(ScheduleImportMode.DraftingTable, options.Mode);
        Assert.Equal(42, options.TargetViewId);
        Assert.Null(options.TargetScheduleId);
        Assert.True(options.CreateNewViewIfNeeded);
        Assert.Equal(1.25, options.TableScale);
        Assert.False(options.DryRun);
    }
}
