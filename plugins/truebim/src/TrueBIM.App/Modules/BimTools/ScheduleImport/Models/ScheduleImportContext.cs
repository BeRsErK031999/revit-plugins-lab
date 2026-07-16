namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportContext(
    string DocumentTitle,
    string ActiveViewName,
    string ActiveViewKind,
    long? ActiveViewId,
    bool CanUseBimScheduleMode,
    IReadOnlyList<string> AvailableBimScheduleParameterNames,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ScheduleTarget> ScheduleTargets);
