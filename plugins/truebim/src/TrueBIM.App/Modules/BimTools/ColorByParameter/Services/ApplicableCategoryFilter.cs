namespace TrueBIM.App.Modules.BimTools.ColorByParameter.Services;

public static class ApplicableCategoryFilter
{
    public static IReadOnlyList<long> GetApplicableCategoryIds(
        IEnumerable<long> selectedCategoryIds,
        IReadOnlyCollection<long> applicableCategoryIds)
    {
        long[] selectedIds = selectedCategoryIds
            .Distinct()
            .ToArray();

        if (applicableCategoryIds.Count == 0)
        {
            return selectedIds;
        }

        HashSet<long> applicableIds = applicableCategoryIds.ToHashSet();
        return selectedIds
            .Where(applicableIds.Contains)
            .ToArray();
    }
}
