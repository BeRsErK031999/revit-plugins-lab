namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSheetCollection(
    IReadOnlyList<PrintSheetInfo> Sheets,
    PrintParameterCatalog ParameterCatalog);
