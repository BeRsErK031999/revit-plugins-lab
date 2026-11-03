namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintPdfExportResult(
    IReadOnlyList<string> ExportedFiles,
    IReadOnlyList<PrintPdfExportFailure> Failures)
{
    public IReadOnlyDictionary<long, string> LargerPaper { get; init; } = new Dictionary<long, string>();

    public bool Succeeded => Failures.Count == 0;
}
