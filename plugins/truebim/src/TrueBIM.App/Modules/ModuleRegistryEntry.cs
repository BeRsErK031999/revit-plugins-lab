using TrueBIM.App.UI;

namespace TrueBIM.App.Modules;

public sealed record ModuleRegistryEntry(
    ITrueBimModule Implementation,
    ModuleManifest Manifest,
    bool IsEnabled)
{
    public string Id => Manifest.Id;

    public string DisplayName => Manifest.DisplayName;

    public string Description => Manifest.Description;

    public TrueBimIcon Icon => Implementation.Icon;

    public string Version => Manifest.Version;

    public bool IsEnabledByDefault => Manifest.EnabledByDefault;
}
