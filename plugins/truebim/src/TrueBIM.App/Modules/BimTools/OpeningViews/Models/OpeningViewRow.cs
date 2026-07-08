using System.ComponentModel;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewRow : INotifyPropertyChanged
{
    private bool isSelected;
    private string status;
    private string message;

    public OpeningViewRow(
        long elementId,
        string categoryName,
        string familyName,
        string typeName,
        string levelName,
        string viewName,
        string orientationSource,
        string status,
        string message,
        bool canApply)
    {
        ElementId = elementId;
        CategoryName = categoryName;
        FamilyName = familyName;
        TypeName = typeName;
        LevelName = levelName;
        ViewName = viewName;
        OrientationSource = orientationSource;
        this.status = status;
        this.message = message;
        CanApply = canApply;
        isSelected = canApply;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long ElementId { get; }

    public string CategoryName { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string LevelName { get; }

    public string ViewName { get; }

    public string OrientationSource { get; }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Message)));
        }
    }

    public void ApplyResult(OpeningViewReportRow reportRow)
    {
        Status = reportRow.Status;
        Message = reportRow.Message;
        if (reportRow.Status == OpeningViewStatuses.Created)
        {
            IsSelected = false;
        }
    }
}
