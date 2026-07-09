using TrueBIM.App.Modules.BimTools.FamilyManager.Models;
using TrueBIM.App.Modules.BimTools.FamilyManager.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.FamilyManager;

public sealed class FamilyLibraryTreeBuilderTests
{
    [Fact]
    public void Build_CreatesLibraryFolderCategoryFamilyAndTypeNodes()
    {
        string library = Path.Combine(Path.GetTempPath(), "TrueBimFamilyTree", "Library");
        string familyPath = Path.Combine(library, "Doors", "Door A.rfa");

        IReadOnlyList<FamilyLibraryTreeNode> roots = new FamilyLibraryTreeBuilder().Build(
            [new FamilyLibraryFolder { Path = library, IsEnabled = true }],
            [
                new FamilyFileItem
                {
                    FilePath = familyPath,
                    DirectoryPath = Path.GetDirectoryName(familyPath) ?? string.Empty,
                    Name = "Door A",
                    Category = "Двери",
                    CachedTypes =
                    [
                        new FamilyTypeInfo(0, "900x2100"),
                        new FamilyTypeInfo(0, "1000x2100")
                    ]
                }
            ]);

        FamilyLibraryTreeNode root = Assert.Single(roots);
        Assert.Equal(FamilyLibraryTreeNodeKind.Library, root.Kind);
        Assert.Equal("Library", root.Title);
        Assert.Equal("Library", root.DisplayTitle);

        FamilyLibraryTreeNode folder = Assert.Single(root.Children);
        Assert.Equal(FamilyLibraryTreeNodeKind.Folder, folder.Kind);
        Assert.Equal("Doors", folder.Title);

        FamilyLibraryTreeNode category = Assert.Single(folder.Children);
        Assert.Equal(FamilyLibraryTreeNodeKind.Category, category.Kind);
        Assert.Equal("Двери", category.Title);

        FamilyLibraryTreeNode family = Assert.Single(category.Children);
        Assert.Equal(FamilyLibraryTreeNodeKind.Family, family.Kind);
        Assert.Equal("Door A", family.Title);
        Assert.Equal("Door A", family.DisplayTitle);
        Assert.Equal(familyPath, family.FamilyPath);
        Assert.Equal(familyPath, family.ExplorerPath);
        Assert.NotNull(family.Family);

        Assert.Equal(
            ["1000x2100", "900x2100"],
            family.Children.Select(type => type.Title));
        Assert.All(family.Children, type => Assert.Equal(FamilyLibraryTreeNodeKind.Type, type.Kind));
    }

    [Fact]
    public void Build_GroupsFilesOutsideEnabledLibraries()
    {
        string disabledLibrary = Path.Combine(Path.GetTempPath(), "TrueBimFamilyTree", "Disabled");
        string familyPath = Path.Combine(disabledLibrary, "Chair.rfa");

        IReadOnlyList<FamilyLibraryTreeNode> roots = new FamilyLibraryTreeBuilder().Build(
            [new FamilyLibraryFolder { Path = disabledLibrary, IsEnabled = false }],
            [
                new FamilyFileItem
                {
                    FilePath = familyPath,
                    DirectoryPath = Path.GetDirectoryName(familyPath) ?? string.Empty,
                    Name = "Chair",
                    Category = "Мебель"
                }
            ]);

        FamilyLibraryTreeNode root = Assert.Single(roots);
        Assert.Equal("Вне библиотек", root.Title);
        Assert.Equal(FamilyLibraryTreeNodeKind.Library, root.Kind);
        Assert.Equal("Мебель", Assert.Single(root.Children).Title);
    }

    [Fact]
    public void Build_AddsTypeCatalogNamesToFamilyNodes()
    {
        string library = Path.Combine(Path.GetTempPath(), "TrueBimFamilyTree", "Library");
        string familyPath = Path.Combine(library, "Pipes", "Pipe Elbow.rfa");

        IReadOnlyList<FamilyLibraryTreeNode> roots = new FamilyLibraryTreeBuilder().Build(
            [new FamilyLibraryFolder { Path = library, IsEnabled = true }],
            [
                new FamilyFileItem
                {
                    FilePath = familyPath,
                    DirectoryPath = Path.GetDirectoryName(familyPath) ?? string.Empty,
                    Name = "Pipe Elbow",
                    Category = "Соединительные детали труб",
                    CachedTypes = [new FamilyTypeInfo(0, "DN15")],
                    TypeCatalogTypeNames = ["DN20", "DN15"]
                }
            ]);

        FamilyLibraryTreeNode family = roots.Single()
            .Children.Single()
            .Children.Single()
            .Children.Single();

        Assert.Equal(
            ["DN15", "DN20"],
            family.Children.Select(type => type.Title));
    }
}
