namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarZoneMerge(
    string MergedZoneId,
    IReadOnlyList<string> SourceZoneIds);
