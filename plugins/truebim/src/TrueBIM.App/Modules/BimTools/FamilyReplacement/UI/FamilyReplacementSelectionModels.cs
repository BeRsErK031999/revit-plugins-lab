using System.ComponentModel;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;

public enum FamilyReplacementScope
{
    ActiveView,
    SelectedViews,
    Project,
    CurrentSelection,
    PickElements
}

public sealed record FamilyReplacementViewOption(long Id, string Name, string Kind, bool InitiallySelected);

public sealed record FamilyReplacementCandidate(
    long Id,
    long TypeId,
    long CategoryId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    string LevelName,
    string Mark,
    bool PreferredSource);

public sealed record FamilyReplacementTypeOption(
    long Id,
    long CategoryId,
    string CategoryName,
    string FamilyName,
    string TypeName,
    bool PreferredTarget)
{
    public string Label => $"{FamilyName} : {TypeName}";
}

public sealed class FamilyReplacementCandidateRow : INotifyPropertyChanged
{
    private bool isSelected;

    public FamilyReplacementCandidateRow(FamilyReplacementCandidate item)
    {
        Item = item;
        isSelected = item.PreferredSource;
    }

    public FamilyReplacementCandidate Item { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value)
            {
                return;
            }

            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
