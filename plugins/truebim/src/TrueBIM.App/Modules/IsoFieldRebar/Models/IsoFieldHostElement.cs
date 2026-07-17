namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldHostElement(
    long ElementId,
    string HostKind,
    string HostKindDisplayName,
    string Name,
    IsoFieldHostGeometry? Geometry = null,
    IsoFieldHostGeometryProfile GeometryProfile = IsoFieldHostGeometryProfile.Unknown)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? $"{HostKindDisplayName} (ID {ElementId})"
        : $"{HostKindDisplayName}: {Name} (ID {ElementId})";

    public bool IsSlab => string.Equals(HostKind, "Slab", StringComparison.Ordinal);

    public bool IsWall => string.Equals(HostKind, "Wall", StringComparison.Ordinal);
}
