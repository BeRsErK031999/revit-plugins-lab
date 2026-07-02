namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintCadExportResult(
    PrintCadExportFormat Format,
    IReadOnlyList<string> ExportedFiles,
    IReadOnlyList<PrintCadExportFailure> Failures)
{
    public bool Succeeded => Failures.Count == 0;
}
