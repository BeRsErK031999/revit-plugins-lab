using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyManagerPaneSummaryBuilder
{
    public FamilyManagerPaneSummary Build(FamilyManagerProfile profile, string folderPath)
    {
        Guard.NotNull(profile, nameof(profile));

        string normalizedFolder = FamilyPathNormalizer.Normalize(folderPath);
        List<FamilyFileItem> families = profile.CachedFiles
            .Where(family => IsUnderFolder(family.FilePath, normalizedFolder))
            .ToList();

        List<string> categories = families
            .Select(family => family.Category)
            .Where(category => !string.IsNullOrWhiteSpace(category))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
            .Take(4)
            .ToList();

        List<string> recentFamilies = families
            .OrderByDescending(family => family.LastWriteTimeUtc)
            .ThenBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(family => family.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .Take(5)
            .ToList();

        return new FamilyManagerPaneSummary(
            normalizedFolder,
            ResolveFolderName(normalizedFolder),
            families.Count,
            families
                .Select(family => family.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Count(),
            families.Count(family => family.MetadataUpdatedAtUtc is not null),
            families.Sum(family => family.CachedTypes.Count),
            families.Count(family => family.IsFavorite),
            profile.CacheUpdatedAtUtc,
            categories,
            recentFamilies);
    }

    private static string ResolveFolderName(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return "Папка не выбрана";
        }

        try
        {
            DirectoryInfo directory = new(folderPath);
            return string.IsNullOrWhiteSpace(directory.Name)
                ? folderPath
                : directory.Name;
        }
        catch (ArgumentException)
        {
            return folderPath;
        }
    }

    private static bool IsUnderFolder(string filePath, string folderPath)
    {
        string normalizedPath = FamilyPathNormalizer.Normalize(filePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        return normalizedPath.Equals(folderPath, StringComparison.CurrentCultureIgnoreCase)
            || normalizedPath.StartsWith(folderPath + Path.DirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase)
            || normalizedPath.StartsWith(folderPath + Path.AltDirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase);
    }
}
