namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyLibraryTreeNode
{
    public FamilyLibraryTreeNode(
        FamilyLibraryTreeNodeKind kind,
        string title,
        string path,
        string familyPath = "",
        string typeName = "")
    {
        Kind = kind;
        Title = title;
        Path = path;
        FamilyPath = familyPath;
        TypeName = typeName;
    }

    public FamilyLibraryTreeNodeKind Kind { get; }

    public string Title { get; }

    public string Path { get; }

    public string FamilyPath { get; }

    public string TypeName { get; }

    public List<FamilyLibraryTreeNode> Children { get; } = [];

    public string DisplayTitle => Kind is FamilyLibraryTreeNodeKind.Type || Children.Count == 0
        ? Title
        : $"{Title} {Children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public string ExplorerPath => !string.IsNullOrWhiteSpace(FamilyPath)
        ? FamilyPath
        : Path;
}
