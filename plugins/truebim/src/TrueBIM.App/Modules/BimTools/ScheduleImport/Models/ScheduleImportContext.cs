namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportContext(
    string DocumentTitle,
    string ActiveViewName,
    string ActiveViewKind,
    long? ActiveViewId,
    bool CanUseDraftingTableMode,
    bool CanUseBimScheduleMode,
    IReadOnlyList<string> AvailableBimScheduleParameterNames,
    IReadOnlyList<string> Warnings);
