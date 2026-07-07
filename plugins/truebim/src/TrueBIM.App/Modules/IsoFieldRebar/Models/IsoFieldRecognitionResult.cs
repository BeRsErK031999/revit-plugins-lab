namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRecognitionResult(
    IReadOnlyList<IsoFieldPolyline> Polylines,
    IReadOnlyList<string> Diagnostics)
{
    public static IsoFieldRecognitionResult Empty { get; } = new(
        Array.Empty<IsoFieldPolyline>(),
        Array.Empty<string>());
}
