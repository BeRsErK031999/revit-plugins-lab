using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Revit;

internal sealed class FamilyReplacementFailureHandler : IFailuresPreprocessor
{
    private readonly bool ignoreIntersectionWarnings;
    private readonly List<string> ignored = new();

    public FamilyReplacementFailureHandler(bool ignoreIntersectionWarnings) =>
        this.ignoreIntersectionWarnings = ignoreIntersectionWarnings;

    public string Description { get; private set; } = string.Empty;
    public string IgnoredDescription => string.Join("; ", ignored.Distinct());

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        List<string> blocking = new();
        foreach (FailureMessageAccessor message in accessor.GetFailureMessages())
        {
            if (ignoreIntersectionWarnings && message.GetSeverity() == FailureSeverity.Warning
                && IsIntersection(message.GetFailureDefinitionId()))
            {
                ignored.Add(message.GetDescriptionText());
                accessor.DeleteWarning(message);
            }
            else
                blocking.Add(message.GetDescriptionText());
        }

        if (blocking.Count == 0)
            return FailureProcessingResult.Continue;
        Description = "Revit отменил замену: " + string.Join("; ", blocking.Distinct());
        return FailureProcessingResult.ProceedWithRollBack;
    }

    private static bool IsIntersection(FailureDefinitionId id) =>
        id == BuiltInFailures.OverlapFailures.DuplicateInstances
        || id == BuiltInFailures.OverlapFailures.OverlappingElementsTest
        || id == BuiltInFailures.OverlapFailures.WallsOverlap
        || id == BuiltInFailures.OverlapFailures.FloorsOverlap
        || id == BuiltInFailures.JoinElementsFailures.JoiningDisjointWarn
        || id == BuiltInFailures.JoinElementsFailures.JoiningDisjoint
        || id == BuiltInFailures.JoinElementsFailures.CannotJoinElementsWarn;
}
