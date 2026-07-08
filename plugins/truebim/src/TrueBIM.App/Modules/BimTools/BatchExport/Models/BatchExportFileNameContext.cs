namespace TrueBIM.App.Modules.BimTools.BatchExport.Models;

public sealed record BatchExportFileNameContext(
    string DocumentName,
    string ProjectName,
    string ProjectNumber,
    DateTime ExportDate,
    IReadOnlyDictionary<string, string> ProjectParameters);
