namespace TrueBIM.App.Modules.BimTools.AutoTags.Models;

public sealed record AutoTagReportRow(
    string Phase,
    string ViewName,
    long ElementId,
    string CategoryName,
    string ElementName,
    string TagTypeName,
    string Status,
    string Message);
