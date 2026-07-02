namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintPdfExportFailure(
    PrintPdfExportItem Item,
    string Message);
