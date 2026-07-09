using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyLibraryAuditService
{
    public IReadOnlyList<FamilyLibraryAuditIssue> Audit(
        IReadOnlyList<FamilyFileItem> families,
        IReadOnlyList<FamilyLibraryFolder> libraryFolders)
    {
        if (families is null)
        {
            throw new ArgumentNullException(nameof(families));
        }

        if (libraryFolders is null)
        {
            throw new ArgumentNullException(nameof(libraryFolders));
        }

        List<FamilyLibraryAuditIssue> issues = [];
        List<string> enabledFolderPaths = libraryFolders
            .Where(folder => folder.IsEnabled)
            .Select(folder => FamilyPathNormalizer.Normalize(folder.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (FamilyFileItem family in families)
        {
            AddFileIssues(family, issues);
        }

        AddDuplicateIssues(families, enabledFolderPaths, issues);
        return issues
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.KindDisplay, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(issue => issue.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(issue => issue.FilePath, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddFileIssues(FamilyFileItem family, ICollection<FamilyLibraryAuditIssue> issues)
    {
        string filePath = FamilyPathNormalizer.Normalize(family.FilePath);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (!File.Exists(filePath))
        {
            issues.Add(CreateIssue(
                FamilyLibraryAuditSeverity.Error,
                FamilyLibraryAuditIssueKind.MissingFile,
                family,
                "Файл есть в cache, но отсутствует на диске.",
                filePath));
            return;
        }

        FileInfo fileInfo = new(filePath);
        if (family.MetadataUpdatedAtUtc is null)
        {
            issues.Add(CreateIssue(
                FamilyLibraryAuditSeverity.Warning,
                FamilyLibraryAuditIssueKind.StaleMetadata,
                family,
                "Метаданные ещё не обновлялись.",
                filePath));
        }
        else if (fileInfo.LastWriteTimeUtc > family.MetadataUpdatedAtUtc.Value.UtcDateTime)
        {
            issues.Add(CreateIssue(
                FamilyLibraryAuditSeverity.Warning,
                FamilyLibraryAuditIssueKind.StaleMetadata,
                family,
                "Файл изменён после обновления metadata-cache.",
                filePath));
        }

        if (string.IsNullOrWhiteSpace(family.Category)
            || string.Equals(family.Category, FamilyManagerDefaults.UnknownCategory, StringComparison.CurrentCultureIgnoreCase))
        {
            issues.Add(CreateIssue(
                FamilyLibraryAuditSeverity.Info,
                FamilyLibraryAuditIssueKind.EmptyCategory,
                family,
                "Категория не определена.",
                filePath));
        }

        if (family.MetadataUpdatedAtUtc is not null && family.CachedTypes.Count == 0)
        {
            issues.Add(CreateIssue(
                FamilyLibraryAuditSeverity.Info,
                FamilyLibraryAuditIssueKind.MissingTypes,
                family,
                "В metadata-cache нет типов семейства.",
                filePath));
        }
    }

    private static void AddDuplicateIssues(
        IReadOnlyList<FamilyFileItem> families,
        IReadOnlyList<string> enabledFolderPaths,
        ICollection<FamilyLibraryAuditIssue> issues)
    {
        AddGroupIssues(
            families
                .Where(family => !string.IsNullOrWhiteSpace(family.Name))
                .GroupBy(family => family.Name.Trim(), StringComparer.CurrentCultureIgnoreCase),
            FamilyLibraryAuditIssueKind.DuplicateName,
            "Имя семейства повторяется в библиотеке.",
            issues);

        AddGroupIssues(
            families
                .Where(family => family.SizeBytes > 0 && family.LastWriteTimeUtc != default)
                .GroupBy(
                    family => $"{family.Name.Trim()}|{family.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{family.LastWriteTimeUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    StringComparer.CurrentCultureIgnoreCase),
            FamilyLibraryAuditIssueKind.DuplicateSignature,
            "Совпадают имя, размер и дата изменения файла.",
            issues);

        AddGroupIssues(
            families
                .Select(family => new
                {
                    Family = family,
                    RelativePath = GetRelativePath(family.FilePath, enabledFolderPaths)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.RelativePath))
                .GroupBy(item => item.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => group.Select(item => item.Family)),
            FamilyLibraryAuditIssueKind.DuplicateRelativePath,
            "Одинаковый относительный путь встречается в нескольких источниках библиотеки.",
            issues);
    }

    private static void AddGroupIssues(
        IEnumerable<IEnumerable<FamilyFileItem>> groups,
        FamilyLibraryAuditIssueKind kind,
        string message,
        ICollection<FamilyLibraryAuditIssue> issues)
    {
        foreach (List<FamilyFileItem> group in groups.Select(group => group.ToList()).Where(group => group.Count > 1))
        {
            FamilyFileItem first = group
                .OrderBy(family => family.FilePath, StringComparer.CurrentCultureIgnoreCase)
                .First();
            issues.Add(CreateIssue(
                FamilyLibraryAuditSeverity.Warning,
                kind,
                first,
                message,
                FamilyPathNormalizer.Normalize(first.FilePath),
                group.Count,
                BuildGroupKey(group)));
        }
    }

    private static FamilyLibraryAuditIssue CreateIssue(
        FamilyLibraryAuditSeverity severity,
        FamilyLibraryAuditIssueKind kind,
        FamilyFileItem family,
        string message,
        string filePath,
        int relatedCount = 1,
        string groupKey = "")
    {
        return new FamilyLibraryAuditIssue
        {
            Severity = severity,
            Kind = kind,
            FamilyName = string.IsNullOrWhiteSpace(family.Name)
                ? Path.GetFileNameWithoutExtension(filePath)
                : family.Name,
            Message = message,
            FilePath = filePath,
            RelatedCount = relatedCount,
            GroupKey = groupKey
        };
    }

    private static string BuildGroupKey(IEnumerable<FamilyFileItem> families)
    {
        return string.Join(
            Environment.NewLine,
            families
                .Select(family => FamilyPathNormalizer.Normalize(family.FilePath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase));
    }

    private static string GetRelativePath(string filePath, IReadOnlyList<string> rootPaths)
    {
        string normalizedFilePath = FamilyPathNormalizer.Normalize(filePath);
        if (string.IsNullOrWhiteSpace(normalizedFilePath))
        {
            return string.Empty;
        }

        foreach (string rootPath in rootPaths)
        {
            if (!IsPathUnderRoot(normalizedFilePath, rootPath))
            {
                continue;
            }

            try
            {
                Uri rootUri = new(EnsureTrailingSeparator(rootPath));
                Uri fileUri = new(normalizedFilePath);
                return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString())
                    .Replace('/', Path.DirectorySeparatorChar);
            }
            catch (UriFormatException)
            {
                return Path.GetFileName(normalizedFilePath);
            }
        }

        return string.Empty;
    }

    private static bool IsPathUnderRoot(string filePath, string rootPath)
    {
        string rootWithSeparator = EnsureTrailingSeparator(rootPath);
        return filePath.StartsWith(rootWithSeparator, StringComparison.CurrentCultureIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
