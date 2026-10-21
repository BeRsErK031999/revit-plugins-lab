using System.ComponentModel;

namespace TrueBIM.App.Modules.RebarFill.Models;

public class RebarFillSelectionItem : INotifyPropertyChanged
{
    private bool isSelected = true;

    public long ElementId { get; }
    public long TypeId { get; }
    public string TypeName { get; }
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public RebarFillSelectionItem(long elementId, long typeId, string typeName)
    {
        ElementId = elementId;
        TypeId = typeId;
        TypeName = typeName;
    }
}

public static class RebarFillSelection
{
    public static bool IsVisible(RebarFillSelectionItem item, long? typeId)
        => !typeId.HasValue || item.TypeId == typeId.Value;

    public static IReadOnlyList<long> GetSelectedIds(IEnumerable<RebarFillSelectionItem> items, long? typeId)
        => items.Where(item => item.IsSelected && IsVisible(item, typeId))
            .Select(item => item.ElementId).Distinct().ToList();
}
