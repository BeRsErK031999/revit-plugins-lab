namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string DocumentTitle,
    string DocumentKey,
    IsoFieldRebarReportHost Host,
    IsoFieldRebarReportProvenance Provenance,
    IsoFieldRebarReportBinding Binding,
    IsoFieldEngineeringSettings EngineeringSettings,
    string RuleProfileSha256,
    IReadOnlyList<IsoFieldRebarReportZone> Zones,
    IReadOnlyList<IsoFieldRebarReportLayerTotal> LayerTotals,
    IsoFieldRebarReportQualityCheck QualityCheck,
    IsoFieldRebarReportChangeSummary ChangeSummary,
    IsoFieldRebarReportApplicationSummary ApplicationSummary,
    IReadOnlyList<string> Diagnostics);

public sealed record IsoFieldRebarReportHost(
    long ElementId,
    string HostKind,
    string HostName);

public sealed record IsoFieldRebarReportProvenance(
    string SourceKind,
    string RecognitionRunner,
    string RecognitionRunnerVersion,
    string PluginVersion,
    string? SourceSetManifestPath,
    IReadOnlyList<IsoFieldRebarReportSourceFile> SourceFiles);

public sealed record IsoFieldRebarReportSourceFile(
    string FileName,
    string FilePath,
    IsoFieldLayerRole? LayerRole,
    int? PixelWidth,
    int? PixelHeight,
    long? SizeBytes,
    DateTimeOffset? LastWriteTimeUtc,
    string? Sha256,
    string Status);

public sealed record IsoFieldRebarReportBinding(
    string Kind,
    double ImageAnchorX,
    double ImageAnchorY,
    double HostAnchorXFeet,
    double HostAnchorYFeet,
    double MillimetersPerPixel,
    double RotationDegrees,
    bool MirrorImageY,
    double? RetainedAreaRatio,
    double? ThirdPointDeviationMillimeters,
    double? ThirdPointToleranceMillimeters,
    DateTimeOffset? ProfileSavedAtUtc);

public sealed record IsoFieldRebarReportComponent(
    double DiameterMillimeters,
    double SpacingMillimeters,
    int CombinationIndex,
    int CombinationCount,
    double AreaSquareCentimetersPerMeter);

public sealed record IsoFieldRebarReportZone(
    string ZoneId,
    string ZoneName,
    IReadOnlyList<string> SourceZoneIds,
    IsoFieldLayerRole? LayerRole,
    IsoFieldRebarFace? Face,
    string Direction,
    IsoFieldReinforcementMode? ReinforcementMode,
    bool IsIncluded,
    bool IsManuallyOverridden,
    bool IsMerged,
    string ReinforcementLabel,
    double? RequiredAreaSquareCentimetersPerMeter,
    double? ProvidedAreaSquareCentimetersPerMeter,
    double GeometryAreaSquareMeters,
    int EstimatedBarCount,
    double? Confidence,
    IsoFieldRebarReviewStatus ReviewStatus,
    int AddCount,
    int UpdateCount,
    int DeleteCount,
    int UnchangedCount,
    IReadOnlyList<IsoFieldRebarReportComponent> Components,
    IReadOnlyList<string> Diagnostics);

public sealed record IsoFieldRebarReportLayerTotal(
    IsoFieldLayerRole LayerRole,
    int ZoneCount,
    int IncludedZoneCount,
    int ExcludedZoneCount,
    int MergedZoneCount,
    double IncludedGeometryAreaSquareMeters,
    int EstimatedBarCount,
    double? MinimumRequiredAreaSquareCentimetersPerMeter,
    double? MaximumRequiredAreaSquareCentimetersPerMeter,
    double? MinimumProvidedAreaSquareCentimetersPerMeter,
    int AddCount,
    int UpdateCount,
    int DeleteCount,
    int UnchangedCount,
    int DiagnosticCount);

public sealed record IsoFieldRebarReportQualityCheck(
    bool Evaluated,
    int BlockingErrorCount,
    int WarningCount,
    bool WarningsAccepted,
    string? Fingerprint,
    IReadOnlyList<IsoFieldRebarReportQualityCoverage> LayerCoverage,
    IReadOnlyList<IsoFieldRebarReportQualityIssue> Issues);

public sealed record IsoFieldRebarReportQualityCoverage(
    IsoFieldLayerRole LayerRole,
    int IncludedZoneCount,
    double CoveredAreaSquareMeters,
    double HostAreaSquareMeters,
    double CoverageRatio);

public sealed record IsoFieldRebarReportQualityIssue(
    IsoFieldRebarQualityCode Code,
    IsoFieldRebarQualitySeverity Severity,
    string Message,
    IsoFieldLayerRole? LayerRole,
    IReadOnlyList<string> ZoneIds,
    double? MeasuredValue,
    double? LimitValue);

public sealed record IsoFieldRebarReportChangeSummary(
    bool Compared,
    bool CanApply,
    int AddCount,
    int UpdateCount,
    int DeleteCount,
    int UnchangedCount);

public sealed record IsoFieldRebarReportApplicationSummary(
    bool Applied,
    DateTimeOffset? CompletedAtUtc,
    int AddedCount,
    int UpdatedCount,
    int DeletedCount,
    int UnchangedCount,
    IReadOnlyList<long> CreatedElementIds,
    IReadOnlyList<long> DeletedElementIds);

public sealed record IsoFieldRebarReportSourceInput(
    string FilePath,
    IsoFieldLayerRole? LayerRole = null,
    int? PixelWidth = null,
    int? PixelHeight = null);

public sealed record IsoFieldRebarReportRequest(
    string DocumentTitle,
    string DocumentKey,
    IsoFieldHostElement Host,
    RebarRulePreviewResult Preview,
    IsoFieldRecognitionResult Recognition,
    IReadOnlyList<IsoFieldRebarReportSourceInput> SourceFiles,
    string SourceKind,
    string RecognitionRunner,
    string RecognitionRunnerVersion,
    string PluginVersion,
    IsoFieldCalibration Calibration,
    IsoFieldSlabBindingAnalysis? SlabBinding = null,
    IsoFieldSlabBindingProfile? BindingProfile = null,
    IsoFieldRebarChangePlan? ChangePlan = null,
    string? SourceSetManifestPath = null,
    IsoFieldRebarQualityResult? QualityResult = null,
    bool QualityWarningsAccepted = false,
    IsoFieldRebarCreationResult? ApplicationResult = null,
    DateTimeOffset? ApplicationCompletedAtUtc = null,
    DateTimeOffset? GeneratedAtUtc = null);

public sealed record IsoFieldRebarReportSaveResult(
    string JsonPath,
    string CsvPath);
