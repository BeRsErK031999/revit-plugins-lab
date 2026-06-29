namespace TrueBIM.App.Modules;

public sealed record ModuleManifestLoadResult(
    IReadOnlyList<ModuleManifest> Manifests,
    int InvalidManifestCount,
    bool ManifestRootExists);
