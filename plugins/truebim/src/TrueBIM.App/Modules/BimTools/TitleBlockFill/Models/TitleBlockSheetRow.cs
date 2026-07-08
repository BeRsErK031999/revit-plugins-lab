using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

public sealed class TitleBlockSheetRow : INotifyPropertyChanged
{
    private bool isSelected;
    private string titleBlockStatus = string.Empty;
    private string previewStatus = string.Empty;
    private string applyStatus = string.Empty;

    public TitleBlockSheetRow(SheetInfo sheet)
    {
        Sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        isSelected = !sheet.IsPlaceholder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SheetInfo Sheet { get; }

    public long ElementId => Sheet.ElementId;

    public string SheetNumber => Sheet.CurrentNumber;

    public string SheetName => Sheet.Name;

    public bool IsPlaceholder => Sheet.IsPlaceholder;

    public string PlaceholderStatus => IsPlaceholder ? "Заглушка" : "Лист";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            bool normalizedValue = value && !IsPlaceholder;
            SetField(ref isSelected, normalizedValue);
        }
    }

    public string TitleBlockStatus
    {
        get => titleBlockStatus;
        set => SetField(ref titleBlockStatus, value);
    }

    public string PreviewStatus
    {
        get => previewStatus;
        set => SetField(ref previewStatus, value);
    }

    public string ApplyStatus
    {
        get => applyStatus;
        set => SetField(ref applyStatus, value);
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
