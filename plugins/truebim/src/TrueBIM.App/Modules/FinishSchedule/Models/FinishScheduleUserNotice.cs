namespace TrueBIM.App.Modules.FinishSchedule.Models;

public enum FinishScheduleUserNoticeSeverity
{
    Info,
    Success,
    Warning,
    Danger
}

public sealed class FinishScheduleUserNotice
{
    public FinishScheduleUserNotice(
        string title,
        string message,
        IEnumerable<string> summaryItems,
        IEnumerable<string> warningItems,
        int issueCount,
        FinishScheduleUserNoticeSeverity severity)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Notice title must not be empty.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Notice message must not be empty.", nameof(message));
        }

        if (issueCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(issueCount));
        }

        Title = title.Trim();
        Message = message.Trim();
        SummaryItems = Normalize(summaryItems, nameof(summaryItems));
        WarningItems = Normalize(warningItems, nameof(warningItems));
        IssueCount = issueCount;
        Severity = severity;
    }

    public string Title { get; }

    public string Message { get; }

    public IReadOnlyList<string> SummaryItems { get; }

    public IReadOnlyList<string> WarningItems { get; }

    public int IssueCount { get; }

    public FinishScheduleUserNoticeSeverity Severity { get; }

    private static IReadOnlyList<string> Normalize(
        IEnumerable<string> items,
        string parameterName)
    {
        return (items ?? throw new ArgumentNullException(parameterName))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
