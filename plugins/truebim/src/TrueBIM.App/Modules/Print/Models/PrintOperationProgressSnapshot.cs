namespace TrueBIM.App.Modules.Print.Models;

public enum PrintOperationProgressUnit
{
    File,
    Sheet
}

public sealed record PrintOperationProgressSnapshot(
    int CompletedCount,
    int TotalCount,
    int RemainingCount,
    PrintOperationProgressUnit Unit,
    string OperationName,
    string ItemName);
