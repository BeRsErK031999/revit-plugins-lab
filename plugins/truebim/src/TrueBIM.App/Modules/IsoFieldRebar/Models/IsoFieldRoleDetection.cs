namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldRoleDetectionKind
{
    NotDetected,
    FileName,
    Header,
    FileNameAndHeader,
    Conflict,
    Manual,
    Manifest
}

public sealed record IsoFieldRoleDetection(
    IsoFieldRoleDetectionKind Kind,
    IsoFieldLayerRole? FileNameRole = null,
    IsoFieldLayerRole? HeaderRole = null,
    double? HeaderConfidence = null);
