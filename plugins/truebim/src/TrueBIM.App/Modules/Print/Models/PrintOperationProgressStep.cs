namespace TrueBIM.App.Modules.Print.Models;

public enum PrintOperationProgressPhase
{
    Started,
    Completed
}

public sealed record PrintOperationProgressStep(
    string OperationName,
    string ItemName,
    PrintOperationProgressPhase Phase);
