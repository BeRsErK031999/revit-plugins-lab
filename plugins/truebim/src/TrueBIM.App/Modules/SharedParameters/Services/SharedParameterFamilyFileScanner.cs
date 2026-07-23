using System.IO;

namespace TrueBIM.App.Modules.SharedParameters.Services;

public sealed class SharedParameterFamilyFileScanner
{
    public IReadOnlyList<string> Scan(string folderPath, bool includeSubdirectories)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path is required.", nameof(folderPath));
        }

        string fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Family folder was not found: {fullPath}");
        }

        SearchOption searchOption = includeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;
        return Directory
            .EnumerateFiles(fullPath, "*.rfa", searchOption)
            .Where(IsSupportedFamilyPath)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool IsSupportedFamilyPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !string.Equals(Path.GetExtension(path), ".rfa", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(path);
        if (fileName.StartsWith("~", StringComparison.Ordinal)
            || fileName.EndsWith(".tmp.rfa", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        int lastDot = nameWithoutExtension.LastIndexOf('.');
        if (lastDot >= 0
            && int.TryParse(nameWithoutExtension.Substring(lastDot + 1), out int backupNumber)
            && backupNumber > 0)
        {
            return false;
        }

        return true;
    }
}
