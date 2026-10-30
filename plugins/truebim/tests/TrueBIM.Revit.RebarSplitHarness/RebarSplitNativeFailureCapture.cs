using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;

namespace TrueBIM.Revit.RebarSplitHarness;

// Only the test harness suppresses modal UI. Product failures remain visible to the user.
internal sealed class RebarSplitNativeFailureCapture : IDisposable
{
    private readonly Document document;
    private readonly bool showDialog;
    private readonly bool allowWarnings;

    internal RebarSplitNativeFailureCapture(Document document, bool showDialog = false, bool allowWarnings = false)
    {
        this.document = document;
        this.showDialog = showDialog;
        this.allowWarnings = allowWarnings;
        document.Application.FailuresProcessing += OnFailuresProcessing;
    }

    internal List<NativeFailureObservation> Failures { get; } = new();

    private void OnFailuresProcessing(object sender, FailuresProcessingEventArgs args)
    {
        FailuresAccessor accessor = args.GetFailuresAccessor();
        bool mustRollback = false;
        foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
        {
            // Splitting copies instance marks; repeated fixture groups also share marks.
            if (failure.GetFailureDefinitionId() == BuiltInFailures.GeneralFailures.DuplicateValue
                && failure.GetSeverity() == FailureSeverity.Warning)
            {
                accessor.DeleteWarning(failure);
                continue;
            }
            Failures.Add(new(failure.GetFailureDefinitionId().Guid, failure.GetSeverity().ToString(),
                failure.GetDescriptionText(), failure.GetFailingElementIds().Select(id => id.IntegerValue).ToArray()));
            if (allowWarnings && failure.GetSeverity() == FailureSeverity.Warning && !showDialog)
                accessor.DeleteWarning(failure);
            else mustRollback = true;
        }
        if (showDialog || !mustRollback) return;
        accessor.SetFailureHandlingOptions(accessor.GetFailureHandlingOptions().SetClearAfterRollback(true));
        args.SetProcessingResult(FailureProcessingResult.ProceedWithRollBack);
    }

    internal bool Matches(IEnumerable<Element> sources)
    {
        List<Element> elements = sources.ToList();
        int[] pinned = elements.Where(source => source.Pinned).Select(source => source.Id.IntegerValue).Distinct().OrderBy(id => id).ToArray();
        int[] grouped = elements.Where(source => source.GroupId != ElementId.InvalidElementId)
            .Select(source => source.Id.IntegerValue).Distinct().OrderBy(id => id).ToArray();
        return Matches(BuiltInFailures.EditingFailures.PinnedObjectNotDeleted, pinned)
            && Matches(BuiltInFailures.EditingFailures.DeletePreventedByGroup, grouped)
            && Failures.Count == (pinned.Length > 0 ? 1 : 0) + (grouped.Length > 0 ? 1 : 0);
    }

    private bool Matches(FailureDefinitionId definition, int[] expected) => expected.Length == 0
        ? Failures.All(failure => failure.Definition != definition.Guid)
        : Failures.Any(failure => failure.Definition == definition.Guid && failure.Elements.OrderBy(id => id).SequenceEqual(expected));

    public void Dispose() => document.Application.FailuresProcessing -= OnFailuresProcessing;
}

internal sealed record NativeFailureObservation(Guid Definition, string Severity, string Description, int[] Elements);
