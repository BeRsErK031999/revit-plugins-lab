namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldLegend(
    IReadOnlyList<IsoFieldLegendBand> Bands,
    int PixelY,
    int PixelStartX,
    int PixelEndX,
    IsoFieldLayerRole? LayerRole = null);

public sealed record IsoFieldLegendBand(
    int Index,
    byte Red,
    byte Green,
    byte Blue,
    double StartRatio,
    double EndRatio,
    string? Label = null)
{
    public string HexColor => $"#{Red:X2}{Green:X2}{Blue:X2}";
}
