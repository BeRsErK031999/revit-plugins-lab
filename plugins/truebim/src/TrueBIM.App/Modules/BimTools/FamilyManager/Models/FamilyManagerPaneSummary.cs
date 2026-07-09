namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed record FamilyManagerPaneSummary(
    string FolderPath,
    string FolderName,
    int FamilyCount,
    int CategoryCount,
    int MetadataCount,
    int TypeCount,
    int FavoriteCount,
    DateTimeOffset? CacheUpdatedAtUtc,
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> RecentFamilies)
{
    public string FamiliesDisplay => FamilyCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string CategoriesDisplay => CategoryCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string MetadataDisplay =>
        $"{MetadataCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} / {FamilyCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public string TypesDisplay => TypeCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string FavoritesDisplay => FavoriteCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public string CacheUpdatedDisplay => CacheUpdatedAtUtc is null
        ? "Кэш не обновлялся"
        : CacheUpdatedAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture);
}
