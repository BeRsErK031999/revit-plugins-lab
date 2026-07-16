using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportCreationOptionsFactory
{
    public static ImportOptions Create(
        long? activeViewId,
        bool createNewViewIfNeeded,
        double tableScale)
    {
        return new ImportOptions(
            ScheduleImportMode.DraftingTable,
            TargetViewId: activeViewId,
            CreateNewViewIfNeeded: createNewViewIfNeeded,
            TableScale: tableScale,
            DryRun: false);
    }
}
