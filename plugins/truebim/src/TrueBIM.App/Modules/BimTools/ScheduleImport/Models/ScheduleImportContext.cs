namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportContext(
    string DocumentTitle,
    string ActiveViewName,
    string ActiveViewKind,
    long? ActiveViewId,
    IReadOnlyList<ScheduleCategoryOption> ScheduleCategories,
    IReadOnlyList<string> Warnings);
