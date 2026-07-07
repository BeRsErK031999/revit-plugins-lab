using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.IsoFieldRebar.Revit;

public sealed record IsoFieldRevitPreviewResult(
    int CreatedCount,
    int DeletedCount,
    IReadOnlyList<ElementId> CreatedElementIds,
    string Message);
