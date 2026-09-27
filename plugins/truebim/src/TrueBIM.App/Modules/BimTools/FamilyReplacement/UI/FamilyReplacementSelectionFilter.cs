using TrueBIM.App.Modules.BimTools.FamilyManager.Services;

namespace TrueBIM.App.Modules.BimTools.FamilyReplacement.UI;

public static class FamilyReplacementSelectionFilter
{
    public static IEnumerable<FamilyReplacementCandidateRow> VisibleRows(
        IEnumerable<FamilyReplacementCandidateRow> rows,
        ISet<long> includedTypes,
        string sourceSearch,
        string instanceSearch)
    {
        return rows.Where(row => includedTypes.Contains(row.Item.TypeId)
            && MatchesSource(row.Item, sourceSearch)
            && FamilySearchMatchService.MatchesText(instanceSearch,
                row.Item.Id.ToString(), row.Item.Mark, row.Item.LevelName, row.Item.FamilyName, row.Item.TypeName));
    }

    public static bool MatchesSource(FamilyReplacementCandidate candidate, string search)
    {
        return FamilySearchMatchService.MatchesText(search,
            candidate.CategoryName, candidate.FamilyName, candidate.TypeName);
    }
}
