namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldHostGeometry(
    IsoFieldRebarPoint3D OriginFeet,
    IsoFieldRebarPoint3D AxisX,
    IsoFieldRebarPoint3D AxisY,
    IsoFieldRebarPoint3D Normal,
    IReadOnlyList<IReadOnlyList<IsoFieldPoint>> BoundaryLoopsFeet);
