namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintExportSummary(
    string MainInstruction,
    string MainContent,
    string? ExpandedContent,
    int ExportedFileCount);
