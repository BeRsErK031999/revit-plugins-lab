using System.ComponentModel;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Models;

public sealed class DatumExtentRow : INotifyPropertyChanged
{
    private bool isSelected;
    private string status;
    private string message;
    private string end0Type;
    private string end1Type;

    public DatumExtentRow(
        long elementId,
        string kind,
        string name,
        string end0Type,
        string end1Type,
        int modelCurveCount,
        int viewSpecificCurveCount,
        string status,
        string message,
        bool canApply)
    {
        ElementId = elementId;
        Kind = kind;
        Name = name;
        this.end0Type = end0Type;
        this.end1Type = end1Type;
        ModelCurveCount = modelCurveCount;
        ViewSpecificCurveCount = viewSpecificCurveCount;
        this.status = status;
        this.message = message;
        CanApply = canApply;
        isSelected = canApply;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public long ElementId { get; }

    public string Kind { get; }

    public string Name { get; }

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

    public string End0Type
    {
        get => end0Type;
        private set
        {
            if (end0Type == value)
            {
                return;
            }

            end0Type = value;
            OnPropertyChanged(nameof(End0Type));
        }
    }

    public string End1Type
    {
        get => end1Type;
        private set
        {
            if (end1Type == value)
            {
                return;
            }

            end1Type = value;
            OnPropertyChanged(nameof(End1Type));
        }
    }

    public int ModelCurveCount { get; }

    public int ViewSpecificCurveCount { get; }

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

    public void ApplyResult(DatumExtentReportRow reportRow)
    {
        Status = reportRow.Status;
        Message = reportRow.Message;
        End0Type = reportRow.End0Type;
        End1Type = reportRow.End1Type;
        if (reportRow.Status == DatumExtentStatuses.Done)
        {
            IsSelected = false;
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
