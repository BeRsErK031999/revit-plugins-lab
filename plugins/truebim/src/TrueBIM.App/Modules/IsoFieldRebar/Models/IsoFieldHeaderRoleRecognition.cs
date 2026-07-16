namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldHeaderRoleRecognition(
    IsoFieldLayerRole? Role,
    double Confidence,
    double Margin)
{
    public static IsoFieldHeaderRoleRecognition NotDetected { get; } = new(null, 0, 0);
}
