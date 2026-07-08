namespace TrueBIM.App.Modules.BimTools.ClashReport.Models;

public sealed record ClashReportRow(
    string Operation,
    string ClashId,
    string ClashName,
    string Status,
    string Message);
