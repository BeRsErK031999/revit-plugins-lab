using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

public sealed class RoomFinishParameterWriter
{
    private readonly FinishParameterChangePlanner changePlanner;

    public RoomFinishParameterWriter(FinishParameterChangePlanner changePlanner)
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

    public int Apply(Document document, FinishWritePlan plan)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (plan.HasCriticalIssues)
        {
            throw new InvalidOperationException("Обязательный план записи помещений содержит критические ошибки.");
        }

        if (plan.Changes.Count == 0)
        {
            return 0;
        }

        using Transaction transaction = new(document, "TrueBIM: записать отделку в помещения");
        FinishTransactionStatus.EnsureStarted(transaction);
        try
        {
            int appliedCount = 0;
            foreach (FinishParameterChange change in plan.Changes)
            {
                Parameter parameter = RevitFinishParameterAccess.ResolveWritableParameter(document, change);
                string currentValue = parameter.AsString() ?? string.Empty;
                if (string.Equals(currentValue, change.NewValue, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(currentValue, change.PreviousValue, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{change.Role}: значение элемента {change.ElementId} изменилось после предпросмотра.");
                }

                if (!parameter.Set(change.NewValue))
                {
                    throw new InvalidOperationException(
                        $"{change.Role}: Revit отклонил запись элемента {change.ElementId}.");
                }

                appliedCount++;
            }

            FinishTransactionStatus.EnsureCommitted(transaction);
            return appliedCount;
        }
        catch
        {
            FinishTransactionStatus.RollBackIfStarted(transaction);
            throw;
        }
    }
}
