namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyManagerProfile
{
    public List<FamilyLibraryFolder> LibraryFolders { get; set; } = new();

    public List<string> FavoritePaths { get; set; } = new();

    public List<FamilyLoadHistoryItem> History { get; set; } = new();

    public List<FamilyFileItem> CachedFiles { get; set; } = new();

    public DateTimeOffset? CacheUpdatedAtUtc { get; set; }
}
