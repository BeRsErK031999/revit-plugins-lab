namespace TrueBIM.App.Modules.BimTools.BatchExport.Models;

public sealed record BatchExportReportRow(
    string SheetNumber,
    string SheetName,
    string Format,
    string Status,
    string Message,
    string FilePath);
