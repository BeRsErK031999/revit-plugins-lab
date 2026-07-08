namespace TrueBIM.App.Modules.BimTools.Common.Models;

public sealed record BimReport(
    string Title,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<BimReportEntry> Entries);
