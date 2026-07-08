using TrueBIM.App.Modules.BimTools.ClashReport.Models;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed record ClashTriageResult(
    string Fingerprint,
    string GroupKey,
    ClashPriority Priority,
    double SeverityScore);
