using System.ComponentModel;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Models;

public sealed class AutoTagElementRow : INotifyPropertyChanged
{
    private bool isSelected;
    private string status;
    private string message;
    private int existingTagCount;

    public AutoTagElementRow(
        long elementId,
        long categoryId,
        string categoryName,
        string elementName,
        int existingTagCount,
        string status,
        string message,
        bool canApply)
    {
        ElementId = elementId;
        CategoryId = categoryId;
        CategoryName = categoryName;
        ElementName = elementName;
        this.existingTagCount = existingTagCount;
        this.status = status;
        this.message = message;
        CanApply = canApply;
        isSelected = canApply;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long ElementId { get; }

    public long CategoryId { get; }

    public string CategoryName { get; }

    public string ElementName { get; }

    public bool CanApply { get; }

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
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public int ExistingTagCount
    {
        get => existingTagCount;
        private set
        {
            if (existingTagCount == value)
            {
                return;
            }

            existingTagCount = value;
            OnPropertyChanged(nameof(ExistingTagCount));
        }
    }

    public string Status
    {
        get => status;
        private set
        {
            if (status == value)
            {
                return;
            }

            status = value;
            OnPropertyChanged(nameof(Status));
        }
    }

    public string Message
    {
        get => message;
        private set
        {
            if (message == value)
            {
                return;
            }

            message = value;
            OnPropertyChanged(nameof(Message));
        }
    }

    public void ApplyResult(AutoTagReportRow row)
    {
        Status = row.Status;
        Message = row.Message;
        if (row.Status == AutoTagStatuses.Done)
        {
            ExistingTagCount++;
            IsSelected = false;
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
