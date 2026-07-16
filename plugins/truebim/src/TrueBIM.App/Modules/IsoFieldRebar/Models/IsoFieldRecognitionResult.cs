namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRecognitionResult(
    IReadOnlyList<IsoFieldPolyline> Polylines,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<IsoFieldLegend>? Legends = null)
{
    public IReadOnlyList<IsoFieldLegend> EffectiveLegends => Legends ?? Array.Empty<IsoFieldLegend>();

    public static IsoFieldRecognitionResult Empty { get; } = new(
        Array.Empty<IsoFieldPolyline>(),
        Array.Empty<string>(),
        Array.Empty<IsoFieldLegend>());
}
