namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ImportOptions(
    ScheduleImportMode Mode,
    long? TargetViewId = null,
    long? TargetScheduleId = null,
    bool CreateNewViewIfNeeded = true,
    double TableScale = 1.0,
    long? TextNoteTypeId = null,
    long? LineStyleId = null,
    bool OverwriteExistingValues = false,
    bool DryRun = false);
