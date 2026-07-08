namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;

public sealed record MepDimensionReportRow(
    string Phase,
    string ViewName,
    string CandidateId,
    string CategoryName,
    string DirectionName,
    int ElementCount,
    int ReadyReferenceCount,
    int MissingReferenceCount,
    string Status,
    string Message);
