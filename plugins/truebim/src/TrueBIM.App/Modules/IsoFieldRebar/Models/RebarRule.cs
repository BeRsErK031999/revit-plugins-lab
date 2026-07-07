namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record RebarRule(
    string Name,
    string HostKind,
    string BarTypeName,
    double SpacingMillimeters,
    string? Note = null);
