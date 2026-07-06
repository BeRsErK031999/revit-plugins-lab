namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintFileNameContext(
    string DocumentName,
    string ProjectName,
    string ProjectNumber,
    DateTime ExportDate,
    IReadOnlyDictionary<string, string> ProjectParameters);
