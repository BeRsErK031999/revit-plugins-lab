namespace TrueBIM.App.Modules.IsoFieldRebar.Models;

public enum IsoFieldHostSupportMode
{
    Unsupported,
    LegacyProbe,
    Engineering
}

public sealed record IsoFieldHostSupportResult(
    IsoFieldHostSupportMode Mode,
    string Code,
    string Message)
{
    public bool IsSupported => Mode != IsoFieldHostSupportMode.Unsupported;

    public bool CanCalculateRules => IsSupported;

    public bool CanApplyRebar => IsSupported;

    public bool RequiresPlanarBinding => Mode == IsoFieldHostSupportMode.Engineering;

    public bool RequiresSlabBinding => RequiresPlanarBinding;
}
