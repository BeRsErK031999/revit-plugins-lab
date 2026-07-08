namespace TrueBIM.App.Modules.BimTools.DatumExtents.Models;

public sealed record DatumExtentReportRow(
    string Phase,
    string ViewName,
    long ElementId,
    string Kind,
    string Name,
    string TargetExtentType,
    string End0Type,
    string End1Type,
    string Status,
    string Message);
