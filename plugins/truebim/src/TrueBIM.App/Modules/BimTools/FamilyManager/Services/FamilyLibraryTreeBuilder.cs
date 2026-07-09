using System.IO;
using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilyLibraryTreeBuilder
{
    public IReadOnlyList<FamilyLibraryTreeNode> Build(
        IReadOnlyList<FamilyLibraryFolder> folders,
        IReadOnlyList<FamilyFileItem> families)
    {
        Guard.NotNull(folders, nameof(folders));
        Guard.NotNull(families, nameof(families));

        List<FamilyLibraryFolder> enabledFolders = folders
            .Where(folder => folder.IsEnabled)
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
            .Select(folder => new FamilyLibraryFolder
            {
                Path = FamilyPathNormalizer.Normalize(folder.Path),
                IsEnabled = true
            })
            .GroupBy(folder => folder.Path, FamilyPathNormalizer.Comparer)
            .Select(group => group.First())
            .OrderBy(folder => folder.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        List<FamilyLibraryTreeNode> roots = enabledFolders
            .Select(folder => new FamilyLibraryTreeNode(
                FamilyLibraryTreeNodeKind.Library,
                folder.DisplayName,
                folder.Path))
            .ToList();

        FamilyLibraryTreeNode? outsideRoot = null;
        foreach (FamilyFileItem family in families
            .Where(family => !string.IsNullOrWhiteSpace(family.FilePath))
            .OrderBy(family => family.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            FamilyLibraryTreeNode root = ResolveRoot(family, enabledFolders, roots, ref outsideRoot);
            AddFamily(root, family);
        }

        if (outsideRoot is not null)
        {
            roots.Add(outsideRoot);
        }

        SortTree(roots);
        return roots;
    }

    private static FamilyLibraryTreeNode ResolveRoot(
        FamilyFileItem family,
        IReadOnlyList<FamilyLibraryFolder> folders,
        IReadOnlyList<FamilyLibraryTreeNode> roots,
        ref FamilyLibraryTreeNode? outsideRoot)
    {
        string filePath = FamilyPathNormalizer.Normalize(family.FilePath);
        for (int index = 0; index < folders.Count; index++)
        {
            if (IsUnderFolder(filePath, folders[index].Path))
            {
                return roots[index];
            }
        }

        outsideRoot ??= new FamilyLibraryTreeNode(
            FamilyLibraryTreeNodeKind.Library,
            "Вне библиотек",
            family.DirectoryPath);
        return outsideRoot;
    }

    private static void AddFamily(FamilyLibraryTreeNode root, FamilyFileItem family)
    {
        FamilyLibraryTreeNode folderNode = AddFolderPath(root, family);
        string category = string.IsNullOrWhiteSpace(family.Category)
            ? FamilyManagerDefaults.UnknownCategory
            : family.Category.Trim();
        FamilyLibraryTreeNode categoryNode = GetOrAdd(
            folderNode.Children,
            FamilyLibraryTreeNodeKind.Category,
            category,
            folderNode.Path);

        string familyTitle = string.IsNullOrWhiteSpace(family.Name)
            ? Path.GetFileNameWithoutExtension(family.FilePath)
            : family.Name;
        FamilyLibraryTreeNode familyNode = GetOrAdd(
            categoryNode.Children,
            FamilyLibraryTreeNodeKind.Family,
            familyTitle,
            family.FilePath,
            family.FilePath);

        foreach (string typeName in ResolveTypeNames(family))
        {
            familyNode.Children.Add(new FamilyLibraryTreeNode(
                FamilyLibraryTreeNodeKind.Type,
                typeName,
                family.FilePath,
                family.FilePath,
                typeName));
        }
    }

    private static FamilyLibraryTreeNode AddFolderPath(FamilyLibraryTreeNode root, FamilyFileItem family)
    {
        string rootPath = FamilyPathNormalizer.Normalize(root.Path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        string directoryPath = FamilyPathNormalizer.Normalize(family.DirectoryPath);
        if (string.IsNullOrWhiteSpace(directoryPath) && !string.IsNullOrWhiteSpace(family.FilePath))
        {
            directoryPath = FamilyPathNormalizer.Normalize(Path.GetDirectoryName(family.FilePath) ?? string.Empty);
        }

        if (string.IsNullOrWhiteSpace(rootPath)
            || string.IsNullOrWhiteSpace(directoryPath)
            || !IsUnderFolder(directoryPath, rootPath)
            || directoryPath.Equals(rootPath, StringComparison.CurrentCultureIgnoreCase))
        {
            return root;
        }

        string relativePath = directoryPath.Substring(rootPath.Length).TrimStart(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return root;
        }

        FamilyLibraryTreeNode parent = root;
        string currentPath = rootPath;
        foreach (string segment in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            parent = GetOrAdd(
                parent.Children,
                FamilyLibraryTreeNodeKind.Folder,
                segment,
                currentPath);
        }

        return parent;
    }

    private static IReadOnlyList<string> ResolveTypeNames(FamilyFileItem family)
    {
        HashSet<string> names = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (FamilyTypeInfo type in family.CachedTypes)
        {
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                names.Add(type.Name.Trim());
            }
        }

        foreach (string typeName in family.TypeCatalogTypeNames)
        {
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                names.Add(typeName.Trim());
            }
        }

        return names
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static FamilyLibraryTreeNode GetOrAdd(
        ICollection<FamilyLibraryTreeNode> nodes,
        FamilyLibraryTreeNodeKind kind,
        string title,
        string path,
        string familyPath = "")
    {
        FamilyLibraryTreeNode? existing = nodes.FirstOrDefault(node =>
            node.Kind == kind
            && string.Equals(node.Title, title, StringComparison.CurrentCultureIgnoreCase)
            && string.Equals(node.FamilyPath, familyPath, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        FamilyLibraryTreeNode node = new(kind, title, path, familyPath);
        nodes.Add(node);
        return node;
    }

    private static bool IsUnderFolder(string filePath, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        string normalizedFolder = FamilyPathNormalizer.Normalize(folderPath).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return filePath.Equals(normalizedFolder, StringComparison.CurrentCultureIgnoreCase)
            || filePath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase)
            || filePath.StartsWith(normalizedFolder + Path.AltDirectorySeparatorChar, StringComparison.CurrentCultureIgnoreCase);
    }

    private static void SortTree(IList<FamilyLibraryTreeNode> nodes)
    {
        List<FamilyLibraryTreeNode> sorted = nodes
            .OrderBy(node => node.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        nodes.Clear();
        foreach (FamilyLibraryTreeNode node in sorted)
        {
            SortTree(node.Children);
            nodes.Add(node);
        }
    }
}
