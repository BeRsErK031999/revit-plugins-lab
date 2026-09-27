namespace TrueBIM.App.Modules.FinishSchedule.Revit;

internal sealed class FinishScheduleHeaderFormattingException : InvalidOperationException
{
    public FinishScheduleHeaderFormattingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
