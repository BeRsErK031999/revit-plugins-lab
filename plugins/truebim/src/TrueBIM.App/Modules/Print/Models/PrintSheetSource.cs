using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintSheetSource(
    string SourceId,
    string SourceName,
    Document Document,
    IReadOnlyList<PrintSheetInfo> Sheets);
