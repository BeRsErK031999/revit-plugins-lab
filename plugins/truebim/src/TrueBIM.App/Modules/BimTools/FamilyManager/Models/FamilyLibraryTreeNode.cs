using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Models;

public sealed class FamilyLibraryTreeNode : INotifyPropertyChanged
{
    private bool isSelectedForAction;
    private bool isLoadedInProject;
    private string projectStatus = string.Empty;

    public FamilyLibraryTreeNode(
        FamilyLibraryTreeNodeKind kind,
        string title,
        string path,
        string familyPath = "",
        string typeName = "",
        FamilyFileItem? family = null)
    {
        Kind = kind;
        Title = title;
        Path = path;
        FamilyPath = familyPath;
        TypeName = typeName;
        Family = family;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FamilyLibraryTreeNodeKind Kind { get; }

    public string Title { get; }

    public string Path { get; }

    public string FamilyPath { get; }

    public string TypeName { get; }

    public FamilyFileItem? Family { get; }

    public List<FamilyLibraryTreeNode> Children { get; } = [];

    public string DisplayTitle => Title;

    public bool CanSelectForAction => Kind is FamilyLibraryTreeNodeKind.Family or FamilyLibraryTreeNodeKind.Type;

    public bool ShowsThumbnail => Kind is FamilyLibraryTreeNodeKind.Family;

    public string ThumbnailPath => Family?.ThumbnailPath ?? string.Empty;

    public string Subtitle => Kind switch
    {
        FamilyLibraryTreeNodeKind.Family => Family?.Category ?? string.Empty,
        FamilyLibraryTreeNodeKind.Type => Family?.Category ?? string.Empty,
        _ => string.Empty
    };

    public bool IsSelectedForAction
    {
        get => isSelectedForAction;
        set => SetField(ref isSelectedForAction, value);
    }

    public bool IsLoadedInProject
    {
        get => isLoadedInProject;
        set
        {
            if (SetField(ref isLoadedInProject, value))
            {
                OnPropertyChanged(nameof(HasProjectStatus));
            }
        }
    }

    public string ProjectStatus
    {
        get => projectStatus;
        set
        {
            if (SetField(ref projectStatus, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasProjectStatus));
            }
        }
    }

    public bool HasProjectStatus => !string.IsNullOrWhiteSpace(ProjectStatus);

    public string ExplorerPath => !string.IsNullOrWhiteSpace(FamilyPath)
        ? FamilyPath
        : Path;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
