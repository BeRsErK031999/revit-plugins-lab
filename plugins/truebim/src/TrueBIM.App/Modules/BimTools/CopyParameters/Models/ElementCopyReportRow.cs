namespace TrueBIM.App.Modules.BimTools.CopyParameters.Models;

public sealed record ElementCopyReportRow(
    string TargetElementLabel,
    string ParameterName,
    bool Succeeded,
    string Message);
