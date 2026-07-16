namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSlabRebarSegment(
    string ZoneId,
    IsoFieldLayerRole LayerRole,
    IsoFieldRebarFace Face,
    IsoFieldRebarDirection Direction,
    IsoFieldRebarComponent Component,
    IsoFieldPoint StartFeet,
    IsoFieldPoint EndFeet,
    string StableId)
{
    public double LengthFeet => Math.Sqrt(
        Math.Pow(EndFeet.X - StartFeet.X, 2)
        + Math.Pow(EndFeet.Y - StartFeet.Y, 2));
}
