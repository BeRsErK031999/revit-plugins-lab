using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportViewActivationPolicy
{
    public static bool ShouldOpenCreatedSchedule(ScheduleImportCreationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return result.Succeeded
            && result.ScheduleId is > 0;
    }
}
