namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarPlacement(
    string ZoneId,
    string ZoneName,
    RebarRule Rule,
    IsoFieldRebarPoint3D Start,
    IsoFieldRebarPoint3D End,
    IsoFieldRebarPoint3D Normal)
{
    public double LengthFeet => Math.Sqrt(
        Math.Pow(End.XFeet - Start.XFeet, 2)
        + Math.Pow(End.YFeet - Start.YFeet, 2)
        + Math.Pow(End.ZFeet - Start.ZFeet, 2));
}
