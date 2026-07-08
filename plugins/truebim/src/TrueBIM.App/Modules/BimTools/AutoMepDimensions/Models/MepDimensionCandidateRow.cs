using System.ComponentModel;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;

public sealed class MepDimensionCandidateRow : INotifyPropertyChanged
{
    private bool isSelected;
    private string status;
    private string message;

    public MepDimensionCandidateRow(
        string candidateId,
        string categoryName,
        string directionName,
        int elementCount,
        int readyReferenceCount,
        int missingReferenceCount,
        string dimensionLine,
        string status,
        string message,
        bool canApply)
    {
        CandidateId = candidateId;
        CategoryName = categoryName;
        DirectionName = directionName;
        ElementCount = elementCount;
        ReadyReferenceCount = readyReferenceCount;
        MissingReferenceCount = missingReferenceCount;
        DimensionLine = dimensionLine;
        this.status = status;
        this.message = message;
        CanApply = canApply;
        isSelected = canApply;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CandidateId { get; }

    public string CategoryName { get; }

    public string DirectionName { get; }

    public int ElementCount { get; }

    public int ReadyReferenceCount { get; }

    public int MissingReferenceCount { get; }

    public string DimensionLine { get; }

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

    public void ApplyResult(MepDimensionReportRow reportRow)
    {
        Status = reportRow.Status;
        Message = reportRow.Message;
        if (reportRow.Status == MepDimensionStatuses.Done)
        {
            IsSelected = false;
        }
    }
}
