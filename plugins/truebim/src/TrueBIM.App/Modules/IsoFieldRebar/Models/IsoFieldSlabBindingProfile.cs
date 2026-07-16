namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public sealed record IsoFieldSlabBindingProfile(
    string DocumentKey,
    long ViewId,
    long HostElementId,
    string HostName,
    IsoFieldSlabBindingInput Binding,
    DateTimeOffset SavedAtUtc);

public sealed class IsoFieldSlabBindingProfileCollection
{
    public int SchemaVersion { get; set; } = 1;

    public List<IsoFieldSlabBindingProfile> Profiles { get; set; } = new();
}
