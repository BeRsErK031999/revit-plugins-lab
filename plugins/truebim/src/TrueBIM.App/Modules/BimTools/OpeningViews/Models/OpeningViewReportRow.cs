namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed record OpeningViewReportRow(
    string Phase,
    string SourceViewName,
    long ElementId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string LevelName,
    string ViewName,
    string Status,
    string Message);
