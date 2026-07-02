namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintPdfExportResult(
    IReadOnlyList<string> ExportedFiles,
    IReadOnlyList<PrintPdfExportFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}
