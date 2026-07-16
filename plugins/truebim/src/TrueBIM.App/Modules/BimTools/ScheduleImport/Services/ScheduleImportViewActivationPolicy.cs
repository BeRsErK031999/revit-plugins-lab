using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportViewActivationPolicy
{
    public static bool ShouldOpenSeparateTab(DraftingTableCreationResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        return result.Succeeded
            && result.CreatedNewView
            && result.TargetViewId is > 0;
    }
}
