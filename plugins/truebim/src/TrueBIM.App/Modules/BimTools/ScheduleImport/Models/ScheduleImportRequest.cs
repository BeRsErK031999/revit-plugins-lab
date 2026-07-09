namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportRequest(
    ParsedTable Table,
    ImportOptions Options);
