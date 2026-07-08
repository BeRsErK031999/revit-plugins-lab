namespace TrueBIM.App.Modules.BimTools.BatchExport.Models;

public sealed record BatchExportFileNamePreview(
    string FileName,
    bool WasTruncated,
    bool HasMissingTokens,
    IReadOnlyList<string> MissingTokens);
