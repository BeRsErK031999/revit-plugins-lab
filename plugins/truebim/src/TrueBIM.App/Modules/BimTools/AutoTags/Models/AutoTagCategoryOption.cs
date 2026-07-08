using System.ComponentModel;
using Autodesk.Revit.DB;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Models;

public sealed class AutoTagCategoryOption : INotifyPropertyChanged
{
    private bool isSelected;

    public AutoTagCategoryOption(ElementId categoryId, string name, int elementCount)
    {
        CategoryId = categoryId;
        Name = string.IsNullOrWhiteSpace(name) ? "<без категории>" : name;
        ElementCount = elementCount;
        isSelected = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ElementId CategoryId { get; }

    public long CategoryIdValue => RevitElementIds.GetValue(CategoryId);

    public string Name { get; }

    public int ElementCount { get; }

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

    public string DisplayName => $"{Name} ({ElementCount})";
}
