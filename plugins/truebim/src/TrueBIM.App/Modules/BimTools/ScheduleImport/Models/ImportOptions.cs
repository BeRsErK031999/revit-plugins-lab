namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ImportOptions(
    ScheduleImportMode Mode,
    double TableScale = 1.0,
    bool OverwriteExistingValues = false,
    bool DryRun = false);
