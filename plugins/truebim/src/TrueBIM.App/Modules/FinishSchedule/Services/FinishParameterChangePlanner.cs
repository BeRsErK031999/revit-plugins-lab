using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishParameterChangePlanner
{
    public FinishWritePlan Create(
        int targetElementCount,
        IEnumerable<FinishParameterWriteCandidate> candidates,
        IEnumerable<FinishWriteIssue>? additionalIssues = null)
    {
        FinishParameterWriteCandidate[] orderedCandidates = (candidates
                ?? throw new ArgumentNullException(nameof(candidates)))
            .OrderBy(candidate => candidate.Target.ElementId)
            .ThenBy(candidate => candidate.Target.Role, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Target.Reference.StableKey, StringComparer.Ordinal)
            .ToArray();
        List<FinishParameterChange> changes = [];
        List<FinishWriteIssue> issues = [.. additionalIssues ?? []];
        int unchangedCount = 0;
        int blockedCount = 0;
        foreach (FinishParameterWriteCandidate candidate in orderedCandidates)
        {
            if (candidate.BlockingCode.HasValue)
            {
                blockedCount++;
                issues.Add(new FinishWriteIssue(
                    candidate.BlockingCode.Value,
                    candidate.Target.IsRequired
                        ? FinishWriteIssueSeverity.Critical
                        : FinishWriteIssueSeverity.Warning,
                    candidate.BlockingMessage!,
                    candidate.Target.ElementId,
                    candidate.Target.Role));
                continue;
            }

            if (string.Equals(candidate.CurrentValue, candidate.Target.Value, StringComparison.Ordinal))
            {
                unchangedCount++;
                continue;
            }

            changes.Add(new FinishParameterChange(
                candidate.Target.ElementId,
                candidate.Target.Reference,
                candidate.Target.Role,
                candidate.CurrentValue,
                candidate.Target.Value,
                candidate.Target.IsRequired,
                candidate.Target.Category));
        }

        return new FinishWritePlan(
            targetElementCount,
            orderedCandidates.Length,
            unchangedCount,
            blockedCount,
            changes,
            issues);
    }
}
