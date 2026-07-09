namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ImportReport(
    string SourceFile,
    ScheduleImportMode Mode,
    int TablesFound,
    int SelectedTable,
    int RowsCount,
    int ColumnsCount,
    int CreatedElementsCount,
    int UpdatedElementsCount,
    int UnresolvedRows,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);
