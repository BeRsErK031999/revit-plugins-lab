namespace TrueBIM.App.Modules.FinishSchedule.Models;

public sealed record FinishScheduleValidationIssue(
    string Code,
    string Field,
    string Message);

public sealed class FinishScheduleValidationResult
{
    public FinishScheduleValidationResult(IEnumerable<FinishScheduleValidationIssue> issues)
    {
        Issues = (issues ?? throw new ArgumentNullException(nameof(issues))).ToArray();
    }

    public IReadOnlyList<FinishScheduleValidationIssue> Issues { get; }

    public bool IsValid => Issues.Count == 0;
}
