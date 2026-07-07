namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldRebarPlacementBounds(
    double MinXFeet,
    double MinYFeet,
    double MinZFeet,
    double MaxXFeet,
    double MaxYFeet,
    double MaxZFeet)
{
    public double WidthXFeet => MaxXFeet - MinXFeet;

    public double WidthYFeet => MaxYFeet - MinYFeet;

    public double WidthZFeet => MaxZFeet - MinZFeet;

    public IsoFieldRebarPoint3D Center => new(
        (MinXFeet + MaxXFeet) / 2,
        (MinYFeet + MaxYFeet) / 2,
        (MinZFeet + MaxZFeet) / 2);
}
