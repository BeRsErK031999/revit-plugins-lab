using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class FinishOwnershipWriter
{
    private readonly FinishParameterChangePlanner changePlanner;

    public FinishOwnershipWriter(FinishParameterChangePlanner changePlanner)
    {
        this.changePlanner = changePlanner ?? throw new ArgumentNullException(nameof(changePlanner));
    }

    public FinishWritePlan Plan(
        Document document,
        FinishParameterTargetBuildResult targets)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (targets is null)
        {
            throw new ArgumentNullException(nameof(targets));
        }

        FinishParameterWriteCandidate[] candidates = targets.Targets
            .Select(target => RevitFinishParameterAccess.Inspect(document, target))
            .ToArray();
        return changePlanner.Create(
            targets.TargetElementCount,
            candidates,
            targets.Issues);
    }

    public FinishOwnershipApplyResult Apply(Document document, FinishWritePlan plan)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (plan.Changes.Count == 0)
        {
            return new FinishOwnershipApplyResult(0, 0, []);
        }

        using Transaction transaction = new(document, "TrueBIM: записать принадлежность отделки");
        FinishTransactionStatus.EnsureStarted(transaction);
        try
        {
            int appliedCount = 0;
            int skippedCount = 0;
            List<string> warnings = [];
            foreach (FinishParameterChange change in plan.Changes)
            {
                try
                {
                    Parameter parameter = RevitFinishParameterAccess.ResolveWritableParameter(document, change);
                    string currentValue = parameter.AsString() ?? string.Empty;
                    if (string.Equals(currentValue, change.NewValue, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.Equals(currentValue, change.PreviousValue, StringComparison.Ordinal))
                    {
                        skippedCount++;
                        warnings.Add(
                            $"{change.Role}: элемент {change.ElementId} пропущен — значение изменилось после предпросмотра.");
                        continue;
                    }

                    if (!parameter.Set(change.NewValue))
                    {
                        skippedCount++;
                        warnings.Add($"{change.Role}: Revit отклонил запись элемента {change.ElementId}.");
                        continue;
                    }

                    appliedCount++;
                }
                catch (Exception exception)
                {
                    skippedCount++;
                    warnings.Add(
                        $"{change.Role}: элемент {change.ElementId} пропущен — {exception.Message}");
                }
            }

            FinishTransactionStatus.EnsureCommitted(transaction);
            return new FinishOwnershipApplyResult(appliedCount, skippedCount, warnings);
        }
        catch
        {
            FinishTransactionStatus.RollBackIfStarted(transaction);
            throw;
        }
    }
}
