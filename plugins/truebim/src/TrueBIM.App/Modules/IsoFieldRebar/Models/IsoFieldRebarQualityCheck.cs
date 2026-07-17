namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldRebarQualitySeverity
{
    Blocking,
    Warning
}

public enum IsoFieldRebarQualityCode
{
    GeometryAnalysisFailed,
    RequiredAreaDeficit,
    SameLayerOverlap,
    FinalGeometryOutsideHost,
    MissingLayerCoverage,
    PartialLayerCoverage,
    ZoneClippedByHost,
    SourceZoneOutsideHost
}

public sealed record IsoFieldRebarQualityIssue(
    IsoFieldRebarQualityCode Code,
    IsoFieldRebarQualitySeverity Severity,
    string Message,
    IsoFieldLayerRole? LayerRole = null,
    IReadOnlyList<string>? ZoneIds = null,
    double? MeasuredValue = null,
    double? LimitValue = null)
{
    public IReadOnlyList<string> EffectiveZoneIds => ZoneIds ?? Array.Empty<string>();
}

public sealed record IsoFieldRebarLayerCoverage(
    IsoFieldLayerRole LayerRole,
    int IncludedZoneCount,
    double CoveredAreaSquareMeters,
    double HostAreaSquareMeters,
    double CoverageRatio);

public sealed record IsoFieldRebarQualityResult(
    IReadOnlyList<IsoFieldRebarQualityIssue> Issues,
    IReadOnlyList<IsoFieldRebarLayerCoverage> LayerCoverage,
    string Fingerprint)
{
    public IReadOnlyList<IsoFieldRebarQualityIssue> BlockingIssues =>
        Issues.Where(issue => issue.Severity == IsoFieldRebarQualitySeverity.Blocking).ToArray();

    public IReadOnlyList<IsoFieldRebarQualityIssue> Warnings =>
        Issues.Where(issue => issue.Severity == IsoFieldRebarQualitySeverity.Warning).ToArray();

    public bool CanCompare => BlockingIssues.Count == 0;

    public bool CanApply(bool warningsAccepted) => CanCompare
        && (Warnings.Count == 0 || warningsAccepted);
}
