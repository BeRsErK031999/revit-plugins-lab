using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyLibraryScanner
{
    private readonly FamilyCategoryGuessService categoryGuessService;

    public FamilyLibraryScanner()
        : this(new FamilyCategoryGuessService())
    {
    }

    public FamilyLibraryScanner(FamilyCategoryGuessService categoryGuessService)
    {
        this.categoryGuessService = categoryGuessService ?? throw new ArgumentNullException(nameof(categoryGuessService));
    }

    public FamilyLibraryScanResult Scan(
        IReadOnlyList<FamilyLibraryFolder> folders,
        ISet<string> favoritePaths,
        IReadOnlyDictionary<string, DateTimeOffset> lastLoadedByPath)
    {
        Guard.NotNull(folders, nameof(folders));
        Guard.NotNull(favoritePaths, nameof(favoritePaths));
        Guard.NotNull(lastLoadedByPath, nameof(lastLoadedByPath));

        List<FamilyFileItem> files = new();
        List<string> warnings = new();
        int scannedFolderCount = 0;
        int missingFolderCount = 0;
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
                    string filePath = FamilyPathNormalizer.Normalize(rawFilePath);
                    if (!seenPaths.Add(filePath))
                    {
                        continue;
                    }

                    FileInfo fileInfo = new(filePath);
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
                        IsFavorite = favoritePaths.Contains(filePath)
                    });
                }
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                warnings.Add($"Не удалось просканировать папку '{folderPath}': {exception.Message}");
            }
        }

        return new FamilyLibraryScanResult(
            files
                .OrderBy(file => file.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            warnings,
            scannedFolderCount,
            missingFolderCount);
    }
}
