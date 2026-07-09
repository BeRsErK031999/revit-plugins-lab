namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed record FamilyLibraryScanResult(
    IReadOnlyList<FamilyFileItem> Files,
    IReadOnlyList<string> Warnings,
    int ScannedFolderCount,
    int MissingFolderCount,
    int ScannedFileCount = 0,
    int MissingFileCount = 0);
