using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyLibraryScanner
{
    private readonly FamilyCategoryGuessService categoryGuessService;
    private readonly FamilyTypeCatalogReader typeCatalogReader;

    public FamilyLibraryScanner()
        : this(new FamilyCategoryGuessService(), new FamilyTypeCatalogReader())
    {
    }

    public FamilyLibraryScanner(FamilyCategoryGuessService categoryGuessService)
        : this(categoryGuessService, new FamilyTypeCatalogReader())
    {
    }

    public FamilyLibraryScanner(FamilyCategoryGuessService categoryGuessService, FamilyTypeCatalogReader typeCatalogReader)
    {
        this.categoryGuessService = categoryGuessService ?? throw new ArgumentNullException(nameof(categoryGuessService));
        this.typeCatalogReader = typeCatalogReader ?? throw new ArgumentNullException(nameof(typeCatalogReader));
    }

    public FamilyLibraryScanResult Scan(
        IReadOnlyList<FamilyLibraryFolder> folders,
        ISet<string> favoritePaths,
        IReadOnlyDictionary<string, DateTimeOffset> lastLoadedByPath)
    {
        return Scan(folders, [], favoritePaths, lastLoadedByPath);
    }

    public FamilyLibraryScanResult Scan(
        IReadOnlyList<FamilyLibraryFolder> folders,
        IReadOnlyList<FamilyLibraryFile> libraryFiles,
        ISet<string> favoritePaths,
        IReadOnlyDictionary<string, DateTimeOffset> lastLoadedByPath)
    {
        Guard.NotNull(folders, nameof(folders));
        Guard.NotNull(libraryFiles, nameof(libraryFiles));
        Guard.NotNull(favoritePaths, nameof(favoritePaths));
        Guard.NotNull(lastLoadedByPath, nameof(lastLoadedByPath));

        List<FamilyFileItem> files = new();
        List<string> warnings = new();
        int scannedFolderCount = 0;
        int missingFolderCount = 0;
        int scannedFileCount = 0;
        int missingFileCount = 0;
        HashSet<string> seenPaths = new(FamilyPathNormalizer.Comparer);

        foreach (FamilyLibraryFolder folder in folders.Where(folder => folder.IsEnabled))
        {
            string folderPath = FamilyPathNormalizer.Normalize(folder.Path);
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                continue;
            }

            if (!Directory.Exists(folderPath))
            {
                missingFolderCount++;
                warnings.Add($"Папка не найдена: {folderPath}");
                continue;
            }

            scannedFolderCount++;
            try
            {
                foreach (string rawFilePath in Directory.EnumerateFiles(folderPath, "*.rfa", SearchOption.AllDirectories))
                {
                    AddFamilyFile(rawFilePath, favoritePaths, lastLoadedByPath, seenPaths, files);
                }
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                warnings.Add($"Не удалось просканировать папку '{folderPath}': {exception.Message}");
            }
        }

        foreach (FamilyLibraryFile libraryFile in libraryFiles.Where(file => file.IsEnabled))
        {
            string filePath = FamilyPathNormalizer.Normalize(libraryFile.Path);
            if (string.IsNullOrWhiteSpace(filePath))
            {
                continue;
            }

            if (!string.Equals(Path.GetExtension(filePath), ".rfa", StringComparison.CurrentCultureIgnoreCase)
                || !File.Exists(filePath))
            {
                missingFileCount++;
                warnings.Add($"Файл семейства не найден: {filePath}");
                continue;
            }

            if (AddFamilyFile(filePath, favoritePaths, lastLoadedByPath, seenPaths, files))
            {
                scannedFileCount++;
            }
        }

        return new FamilyLibraryScanResult(
            files
                .OrderBy(file => file.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            warnings,
            scannedFolderCount,
            missingFolderCount,
            scannedFileCount,
            missingFileCount);
    }

    private bool AddFamilyFile(
        string rawFilePath,
        ISet<string> favoritePaths,
        IReadOnlyDictionary<string, DateTimeOffset> lastLoadedByPath,
        ISet<string> seenPaths,
        ICollection<FamilyFileItem> files)
    {
        string filePath = FamilyPathNormalizer.Normalize(rawFilePath);
        if (!seenPaths.Add(filePath))
        {
            return false;
        }

        FileInfo fileInfo = new(filePath);
        FamilyTypeCatalogInfo typeCatalog = typeCatalogReader.ReadForFamily(filePath);
        lastLoadedByPath.TryGetValue(filePath, out DateTimeOffset lastLoadedAtUtc);
        files.Add(new FamilyFileItem
        {
            FilePath = filePath,
            Name = Path.GetFileNameWithoutExtension(filePath),
            DirectoryPath = fileInfo.DirectoryName ?? string.Empty,
            Category = categoryGuessService.Guess(filePath),
            SizeBytes = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            LastLoadedAtUtc = lastLoadedAtUtc == default ? null : lastLoadedAtUtc,
            TypeCatalogPath = typeCatalog.Path,
            TypeCatalogTypeNames = typeCatalog.TypeNames.ToList(),
            IsFavorite = favoritePaths.Contains(filePath)
        });
        return true;
    }
}
