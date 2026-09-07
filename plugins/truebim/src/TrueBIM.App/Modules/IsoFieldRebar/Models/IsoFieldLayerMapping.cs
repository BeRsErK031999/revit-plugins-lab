namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldRebarDirection
{
    X,
    Y
}

public enum IsoFieldRebarFace
{
    Unconfirmed,
    Bottom,
    Top
}

public sealed record IsoFieldLayerMapping(
    IsoFieldLayerRole Role,
    IsoFieldRebarDirection Direction,
    IsoFieldRebarFace Face)
{
    public static IsoFieldLayerMapping CreateDefault(IsoFieldLayerRole role)
    {
        return new IsoFieldLayerMapping(role, ResolveDirection(role), ResolveDefaultFace(role));
    }

    public static IsoFieldRebarDirection ResolveDirection(IsoFieldLayerRole role)
    {
        return role is IsoFieldLayerRole.As1X or IsoFieldLayerRole.As2X
            ? IsoFieldRebarDirection.X
            : IsoFieldRebarDirection.Y;
    }

    public static IsoFieldRebarFace ResolveDefaultFace(IsoFieldLayerRole role)
    {
        return role is IsoFieldLayerRole.As1X or IsoFieldLayerRole.As3Y
            ? IsoFieldRebarFace.Bottom
            : IsoFieldRebarFace.Top;
    }
}
