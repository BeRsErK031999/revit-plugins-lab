using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

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

    public FinishOwnershipApplyResult Apply(Document document, FinishWritePlan plan,
        IReadOnlyList<FinishRoomAssignment>? roomAssignments = null)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (plan.Changes.Count == 0 && (roomAssignments is null || roomAssignments.Count == 0))
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
                    if (FinishParameterValueComparison.AreEqual(currentValue, change.NewValue))
                    {
                        continue;
                    }

                    if (!FinishParameterValueComparison.AreEqual(currentValue, change.PreviousValue))
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

            foreach (FinishRoomAssignment assignment in roomAssignments ?? [])
            {
                try
                {
                    Parameter parameter = RevitFinishParameterAccess.ResolveWritableParameter(document,
                        new FinishParameterChange(assignment.ElementId, assignment.Reference, "Принадлежность",
                            assignment.Identifier, assignment.Identifier, false));
                    if (!FinishParameterValueComparison.AreEqual(parameter.AsString() ?? string.Empty, assignment.Identifier))
                    {
                        throw new InvalidOperationException("Значение принадлежности изменилось после предпросмотра.");
                    }
                    new FinishOwnershipMetadataService().Write(
                        document.GetElement(RevitElementIds.Create(assignment.ElementId)), assignment);
                }
                catch (Exception exception)
                {
                    skippedCount++;
                    warnings.Add($"Принадлежность: элемент {assignment.ElementId} пропущен — {exception.Message}");
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
