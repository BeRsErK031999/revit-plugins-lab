using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class DuplicateSheetNumberDetector
{
    private static readonly StringComparer SheetNumberComparer = StringComparer.OrdinalIgnoreCase;

    public IReadOnlyList<DuplicateSheetNumberIssue> Detect(
        IReadOnlyList<SheetNumberPreview> previews,
        IReadOnlyList<SheetInfo> existingSheets)
    {
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(existingSheets);

        List<DuplicateSheetNumberIssue> issues = new();
        issues.AddRange(DetectPreviewDuplicates(previews));
        issues.AddRange(DetectExistingDocumentDuplicates(previews, existingSheets));
        return issues;
    }

    private static IEnumerable<DuplicateSheetNumberIssue> DetectPreviewDuplicates(
        IReadOnlyList<SheetNumberPreview> previews)
    {
        return previews
            .GroupBy(preview => preview.PreviewNumber, SheetNumberComparer)
            .Where(group => group.Count() > 1)
            .Select(group => new DuplicateSheetNumberIssue(
                group.First().PreviewNumber,
                DuplicateSheetNumberIssueKind.Preview,
                group.Select(preview => preview.Sheet).ToList()));
    }

    private static IEnumerable<DuplicateSheetNumberIssue> DetectExistingDocumentDuplicates(
        IReadOnlyList<SheetNumberPreview> previews,
        IReadOnlyList<SheetInfo> existingSheets)
    {
        Dictionary<string, IReadOnlyList<SheetInfo>> existingByNumber = existingSheets
            .GroupBy(sheet => sheet.CurrentNumber, SheetNumberComparer)
            .ToDictionary(
                group => group.First().CurrentNumber,
                group => (IReadOnlyList<SheetInfo>)group.ToList(),
                SheetNumberComparer);

        foreach (IGrouping<string, SheetNumberPreview> previewGroup in previews.GroupBy(
            preview => preview.PreviewNumber,
            SheetNumberComparer))
        {
            if (!existingByNumber.TryGetValue(previewGroup.First().PreviewNumber, out IReadOnlyList<SheetInfo>? matches))
            {
                continue;
            }

            HashSet<long> previewSheetIds = previewGroup
                .Select(preview => preview.Sheet.ElementId)
                .ToHashSet();

            List<SheetInfo> existingConflicts = matches
                .Where(sheet => !previewSheetIds.Contains(sheet.ElementId))
                .ToList();

            if (existingConflicts.Count == 0)
            {
                continue;
            }

            List<SheetInfo> conflictingSheets = previewGroup
                .Select(preview => preview.Sheet)
                .Concat(existingConflicts)
                .ToList();

            yield return new DuplicateSheetNumberIssue(
                previewGroup.First().PreviewNumber,
                DuplicateSheetNumberIssueKind.ExistingDocument,
                conflictingSheets);
        }
    }
}
