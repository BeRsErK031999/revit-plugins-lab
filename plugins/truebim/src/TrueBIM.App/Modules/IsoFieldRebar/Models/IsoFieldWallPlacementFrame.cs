namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldWallPlacementFrame(
    IsoFieldRebarPoint3D Center,
    IsoFieldRebarPoint3D Axis,
    IsoFieldRebarPoint3D Normal,
    double LengthFeet,
    double HeightFeet);
