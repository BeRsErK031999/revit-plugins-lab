namespace TrueBIM.App.Modules;

public sealed record ModuleManifest(
    string Id,
    string DisplayName,
    string Description,
    string Version,
    bool EnabledByDefault,
    IReadOnlyList<string> RevitVersions);
