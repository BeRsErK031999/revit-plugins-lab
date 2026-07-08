namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed record FamilyLibraryScanResult(
    IReadOnlyList<FamilyFileItem> Files,
    IReadOnlyList<string> Warnings,
    int ScannedFolderCount,
    int MissingFolderCount);
