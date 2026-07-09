using System.IO;
using TrueBIM.App.Modules.BimTools.Common.Services.Storage;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyManagerProfileStorage
{
    private const int MaxHistoryItems = 40;

    private readonly JsonSettingsStorage storage;
    private readonly string settingsPath;

    public FamilyManagerProfileStorage(ITrueBimLogger logger)
        : this(JsonSettingsStorage.CreateDefaultSettingsPath("family-manager"), logger)
    {
    }

    public FamilyManagerProfileStorage(string settingsPath, ITrueBimLogger logger)
    {
        Guard.NotNullOrWhiteSpace(settingsPath, nameof(settingsPath));

        this.settingsPath = settingsPath;
        storage = new JsonSettingsStorage(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    public string SettingsPath => settingsPath;

    public FamilyManagerProfile Load()
    {
        return Normalize(storage.LoadOrDefault(settingsPath, () => new FamilyManagerProfile()));
    }

    public void Save(FamilyManagerProfile profile)
    {
        storage.Save(settingsPath, Normalize(profile));
    }

    public static FamilyManagerProfile Normalize(FamilyManagerProfile? profile)
    {
        profile ??= new FamilyManagerProfile();
        List<FamilyLibraryFolder> folders = profile.LibraryFolders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
            .Select(folder => new FamilyLibraryFolder
            {
                Path = FamilyPathNormalizer.Normalize(folder.Path),
                IsEnabled = folder.IsEnabled
            })
            .GroupBy(folder => folder.Path, FamilyPathNormalizer.Comparer)
            .Select(group => group.First())
            .OrderBy(folder => folder.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        List<FamilyLibraryFile> libraryFiles = profile.LibraryFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.Path))
            .Select(file => new FamilyLibraryFile
            {
                Path = FamilyPathNormalizer.Normalize(file.Path),
                IsEnabled = file.IsEnabled
            })
            .Where(file => string.Equals(Path.GetExtension(file.Path), ".rfa", StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(file => file.Path, FamilyPathNormalizer.Comparer)
            .Select(group => group.First())
            .OrderBy(file => file.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        List<string> favorites = profile.FavoritePaths
            .Concat(profile.CachedFiles.Where(file => file.IsFavorite).Select(file => file.FilePath))
            .Select(FamilyPathNormalizer.Normalize)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(FamilyPathNormalizer.Comparer)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        List<FamilyLoadHistoryItem> history = profile.History
            .Where(item => !string.IsNullOrWhiteSpace(item.FilePath))
            .Select(item => new FamilyLoadHistoryItem
            {
                FilePath = FamilyPathNormalizer.Normalize(item.FilePath),
                FamilyName = string.IsNullOrWhiteSpace(item.FamilyName)
                    ? Path.GetFileNameWithoutExtension(item.FilePath)
                    : item.FamilyName.Trim(),
                Action = item.Action?.Trim() ?? string.Empty,
                LoadedAtUtc = item.LoadedAtUtc == default ? DateTimeOffset.UtcNow : item.LoadedAtUtc
            })
            .OrderByDescending(item => item.LoadedAtUtc)
            .Take(MaxHistoryItems)
            .ToList();

        Dictionary<string, DateTimeOffset> lastLoadedByPath = history
            .GroupBy(item => item.FilePath, FamilyPathNormalizer.Comparer)
            .ToDictionary(group => group.Key, group => group.Max(item => item.LoadedAtUtc), FamilyPathNormalizer.Comparer);

        List<FamilyFileItem> cachedFiles = profile.CachedFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.FilePath))
            .Select(file =>
            {
                string filePath = FamilyPathNormalizer.Normalize(file.FilePath);
                lastLoadedByPath.TryGetValue(filePath, out DateTimeOffset lastLoadedAtUtc);
                return new FamilyFileItem
                {
                    FilePath = filePath,
                    Name = string.IsNullOrWhiteSpace(file.Name)
                        ? Path.GetFileNameWithoutExtension(filePath)
                        : file.Name.Trim(),
                    DirectoryPath = string.IsNullOrWhiteSpace(file.DirectoryPath)
                        ? Path.GetDirectoryName(filePath) ?? string.Empty
                        : FamilyPathNormalizer.Normalize(file.DirectoryPath),
                    Category = string.IsNullOrWhiteSpace(file.Category)
                        ? FamilyManagerDefaults.UnknownCategory
                        : file.Category.Trim(),
                    SizeBytes = file.SizeBytes,
                    LastWriteTimeUtc = file.LastWriteTimeUtc,
                    LastLoadedAtUtc = lastLoadedAtUtc == default ? file.LastLoadedAtUtc : lastLoadedAtUtc,
                    MetadataUpdatedAtUtc = file.MetadataUpdatedAtUtc,
                    ThumbnailPath = string.IsNullOrWhiteSpace(file.ThumbnailPath)
                        ? string.Empty
                        : FamilyPathNormalizer.Normalize(file.ThumbnailPath),
                    ThumbnailUpdatedAtUtc = file.ThumbnailUpdatedAtUtc,
                    TypeCatalogPath = string.IsNullOrWhiteSpace(file.TypeCatalogPath)
                        ? string.Empty
                        : FamilyPathNormalizer.Normalize(file.TypeCatalogPath),
                    TypeCatalogTypeNames = NormalizeTypeCatalogNames(file.TypeCatalogTypeNames),
                    CachedTypes = NormalizeTypes(file.CachedTypes),
                    IsFavorite = favorites.Contains(filePath),
                    Status = file.Status ?? string.Empty
                };
            })
            .GroupBy(file => file.FilePath, FamilyPathNormalizer.Comparer)
            .Select(group => group.First())
            .OrderBy(file => file.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new FamilyManagerProfile
        {
            LibraryFolders = folders,
            LibraryFiles = libraryFiles,
            FavoritePaths = favorites,
            History = history,
            CachedFiles = cachedFiles,
            CacheUpdatedAtUtc = profile.CacheUpdatedAtUtc
        };
    }

    private static List<FamilyTypeInfo> NormalizeTypes(IEnumerable<FamilyTypeInfo>? types)
    {
        return (types ?? [])
            .Where(type => !string.IsNullOrWhiteSpace(type.Name))
            .Select(type => new FamilyTypeInfo(
                type.ElementId,
                type.Name.Trim(),
                NormalizeTypeParameters(type.Parameters)))
            .GroupBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<string> NormalizeTypeCatalogNames(IEnumerable<string>? typeNames)
    {
        return (typeNames ?? [])
            .Select(typeName => typeName.Trim())
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(typeName => typeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<FamilyTypeParameterInfo> NormalizeTypeParameters(IEnumerable<FamilyTypeParameterInfo>? parameters)
    {
        return (parameters ?? [])
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Name))
            .Select(parameter => new FamilyTypeParameterInfo(
                parameter.Name.Trim(),
                parameter.Value?.Trim() ?? string.Empty,
                parameter.StorageType?.Trim() ?? string.Empty,
                parameter.Scope?.Trim() ?? string.Empty,
                parameter.Formula?.Trim() ?? string.Empty))
            .GroupBy(parameter => $"{parameter.Scope}:{parameter.Name}", StringComparer.CurrentCultureIgnoreCase)
            .Select(group => group.First())
            .OrderBy(parameter => parameter.Scope, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
