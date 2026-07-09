using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyBackupCleanupPreviewService
{
    private static readonly Regex BackupFileNamePattern = new(
        @"^(?<family>.+)\.(?<index>\d{4})\.rfa$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public FamilyBackupCleanupPreviewResult Preview(
        IReadOnlyList<FamilyLibraryFolder> folders,
        IReadOnlyList<FamilyLibraryFile> libraryFiles)
    {
        Guard.NotNull(folders, nameof(folders));
        Guard.NotNull(libraryFiles, nameof(libraryFiles));

        List<FamilyBackupFileItem> files = [];
        List<string> warnings = [];
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
                warnings.Add($"Папка не найдена: {folderPath}");
                continue;
            }

            try
            {
                foreach (string rawFilePath in Directory.EnumerateFiles(folderPath, "*.rfa", SearchOption.AllDirectories))
                {
                    AddBackupFile(rawFilePath, folderPath, seenPaths, files);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                warnings.Add($"Не удалось проверить backup-файлы в папке '{folderPath}': {exception.Message}");
            }
        }

        foreach (FamilyLibraryFile libraryFile in libraryFiles.Where(file => file.IsEnabled))
        {
            string filePath = FamilyPathNormalizer.Normalize(libraryFile.Path);
            if (string.IsNullOrWhiteSpace(filePath) || !IsBackupFamilyFile(filePath))
            {
                continue;
            }

            if (!File.Exists(filePath))
            {
                warnings.Add($"Backup-файл не найден: {filePath}");
                continue;
            }

            AddBackupFile(filePath, filePath, seenPaths, files);
        }

        return new FamilyBackupCleanupPreviewResult(
            files
                .OrderBy(file => file.DirectoryPath, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            warnings);
    }

    public static bool IsBackupFamilyFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string fileName = Path.GetFileName(filePath);
        return !string.IsNullOrWhiteSpace(fileName) && BackupFileNamePattern.IsMatch(fileName);
    }

    private static void AddBackupFile(
        string rawFilePath,
        string sourcePath,
        ISet<string> seenPaths,
        ICollection<FamilyBackupFileItem> files)
    {
        string filePath = FamilyPathNormalizer.Normalize(rawFilePath);
        if (string.IsNullOrWhiteSpace(filePath) || !seenPaths.Add(filePath))
        {
            return;
        }

        if (TryCreateBackupFile(filePath, sourcePath, out FamilyBackupFileItem? file) && file is not null)
        {
            files.Add(file);
        }
    }

    private static bool TryCreateBackupFile(
        string filePath,
        string sourcePath,
        out FamilyBackupFileItem? file)
    {
        file = null;
        string fileName = Path.GetFileName(filePath);
        Match match = BackupFileNamePattern.Match(fileName);
        if (!match.Success)
        {
            return false;
        }

        FileInfo fileInfo = new(filePath);
        if (!fileInfo.Exists)
        {
            return false;
        }

        string familyName = match.Groups["family"].Value;
        if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int backupIndex))
        {
            return false;
        }

        string directoryPath = fileInfo.DirectoryName ?? string.Empty;
        file = new FamilyBackupFileItem
        {
            FilePath = filePath,
            SourcePath = sourcePath,
            Name = fileName,
            FamilyName = familyName,
            PrimaryFilePath = Path.Combine(directoryPath, familyName + ".rfa"),
            DirectoryPath = directoryPath,
            BackupIndex = backupIndex,
            SizeBytes = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
        };
        return true;
    }
}
