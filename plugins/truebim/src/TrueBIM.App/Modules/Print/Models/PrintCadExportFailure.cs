namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintCadExportFailure(
    PrintCadExportFormat Format,
    PrintCadExportItem Item,
    string Message);
