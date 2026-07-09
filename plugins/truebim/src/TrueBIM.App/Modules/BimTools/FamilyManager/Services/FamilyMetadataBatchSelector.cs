using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyMetadataBatchSelector
{
    public IReadOnlyList<FamilyFileItem> SelectFolderScope(
        IReadOnlyList<FamilyFileItem> families,
        string folderPath)
    {
        Guard.NotNull(families, nameof(families));
        Guard.NotNullOrWhiteSpace(folderPath, nameof(folderPath));

        string normalizedFolder = NormalizeFolder(folderPath);
        return families
            .Where(family => IsUnderFolder(family.FilePath, normalizedFolder))
            .GroupBy(family => FamilyPathNormalizer.Normalize(family.FilePath), FamilyPathNormalizer.Comparer)
            .Select(group => group.First())
            .OrderBy(family => family.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsUnderFolder(string filePath, string normalizedFolder)
    {
        string normalizedPath = FamilyPathNormalizer.Normalize(filePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedFolder))
        {
            return false;
        }

        return normalizedPath.Equals(normalizedFolder, StringComparison.CurrentCultureIgnoreCase)
            || normalizedPath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase)
            || normalizedPath.StartsWith(normalizedFolder + Path.AltDirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string NormalizeFolder(string folderPath)
    {
        return FamilyPathNormalizer.Normalize(folderPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }
}
