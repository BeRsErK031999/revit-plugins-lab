using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSheetSource(
    string SourceId,
    string SourceName,
    PrintSheetSourceKind SourceKind,
    Document Document,
    IReadOnlyList<PrintSheetInfo> Sheets,
    PrintParameterCatalog? ParameterCatalog = null)
{
    public bool IsLinked => SourceKind == PrintSheetSourceKind.LinkedDocument;

    public PrintParameterCatalog AvailableParameters => ParameterCatalog ?? PrintParameterCatalog.Empty;
}
