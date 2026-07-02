namespace TrueBIM.App.Modules.Print.Models;

public sealed record PrintFileNamePreview(
    string FileName,
    bool WasTruncated,
    bool HasUnknownTokens);
