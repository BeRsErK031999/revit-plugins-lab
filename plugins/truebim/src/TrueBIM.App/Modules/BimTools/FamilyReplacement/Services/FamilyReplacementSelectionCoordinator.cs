using TrueBIM.App.Modules.BimTools.FamilyReplacement.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.Services;

public interface IFamilyReplacementSelectionContext
{
    IReadOnlyList<long> GetSelectedIds();
    void SetSelectedIds(IReadOnlyList<long> ids);
    void RefreshView();
    bool ElementExists(long id);
}

public static class FamilyReplacementSelectionCoordinator
{
    public static FamilyReplacementResult Execute(
        IFamilyReplacementSelectionContext selection,
        Func<FamilyReplacementResult> replace,
        Action<Exception> onRestoreFailure)
    {
        long[] originalSelection = selection.GetSelectedIds().Distinct().ToArray();
        FamilyReplacementResult? result = null;
        try
        {
            // The operation's source IDs are captured by the caller. Canvas selection is
            // cleared independently so its controls do not participate in cascade deletion.
            selection.SetSelectedIds(Array.Empty<long>());
            selection.RefreshView();
            result = replace();
            return result;
        }
        finally
        {
            try
            {
                Dictionary<long, long> replacements = result?.Items
                    .Where(item => item.Replaced && item.NewId.HasValue)
                    .ToDictionary(item => item.SourceId, item => item.NewId!.Value) ?? new();
                long[] restored = originalSelection
                    .Select(id => replacements.TryGetValue(id, out long replacement) ? replacement : id)
                    .Where(selection.ElementExists).Distinct().ToArray();
                selection.SetSelectedIds(restored);
                selection.RefreshView();
            }
            catch (Exception exception)
            {
                // UI restoration must not turn a committed replacement into a reported failure.
                onRestoreFailure(exception);
            }
        }
    }
}
