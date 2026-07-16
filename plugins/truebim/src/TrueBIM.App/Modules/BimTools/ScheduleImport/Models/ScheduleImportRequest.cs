namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

public sealed record ScheduleImportRequest(
    ParsedTable Table,
    long CategoryId,
    string CategoryName,
    IReadOnlyList<ScheduleFieldMapping> Mappings,
    bool PreviewOnly,
    string ConfigurationFingerprint);
