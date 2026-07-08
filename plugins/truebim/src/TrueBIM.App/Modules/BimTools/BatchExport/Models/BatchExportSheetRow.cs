using System.ComponentModel;
using System.Runtime.CompilerServices;
using TrueBIM.App.Modules.Print.Models;

namespace TrueBIM.App.Modules.BimTools.BatchExport.Models;

public sealed class BatchExportSheetRow : INotifyPropertyChanged
{
    private bool isSelected;
    private string fileNamePreview = string.Empty;
    private string status = string.Empty;
    private bool isFileNameDuplicate;
    private bool isFileNameTruncated;
    private bool hasMissingTokens;

    public BatchExportSheetRow(PrintSheetInfo sheet)
    {
        Sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        isSelected = sheet.CanBePrinted;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PrintSheetInfo Sheet { get; }

    public long ElementId => Sheet.ElementId;

    public string SheetNumber => Sheet.SheetNumber;

    public string SheetName => Sheet.SheetName;

    public string SheetFormat => Sheet.SheetFormat;

    public bool CanBePrinted => Sheet.CanBePrinted;

    public string PrintableStatus => Sheet.CanBePrinted
        ? "Да"
        : Sheet.IsPlaceholder ? "Заглушка" : "Нет";

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            bool normalizedValue = value && CanBePrinted;
            if (isSelected == normalizedValue)
            {
                return;
            }

            isSelected = normalizedValue;
            OnPropertyChanged();
        }
    }

    public string FileNamePreview
    {
        get => fileNamePreview;
        private set => SetField(ref fileNamePreview, value);
    }

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    public bool IsFileNameDuplicate
    {
        get => isFileNameDuplicate;
        set => SetField(ref isFileNameDuplicate, value);
    }

    public bool IsFileNameTruncated
    {
        get => isFileNameTruncated;
        private set => SetField(ref isFileNameTruncated, value);
    }

    public bool HasMissingTokens
    {
        get => hasMissingTokens;
        private set => SetField(ref hasMissingTokens, value);
    }

    public void ApplyPreview(BatchExportFileNamePreview preview)
    {
        FileNamePreview = preview.FileName;
        IsFileNameTruncated = preview.WasTruncated;
        HasMissingTokens = preview.HasMissingTokens;
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
