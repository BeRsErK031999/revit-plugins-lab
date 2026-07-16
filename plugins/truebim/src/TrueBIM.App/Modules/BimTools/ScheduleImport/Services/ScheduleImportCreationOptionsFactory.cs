using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportCreationOptionsFactory
{
    public static ImportOptions Create(double tableScale)
    {
        return new ImportOptions(
            ScheduleImportMode.RevitSchedule,
            TableScale: tableScale,
            DryRun: false);
    }
}
