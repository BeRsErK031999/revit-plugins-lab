namespace TrueBIM.App.Modules.BimTools.OpeningViews.Models;

public sealed class OpeningViewMetadata
{
    public OpeningViewMetadata(
        string sourceElementUniqueId,
        long sourceElementId,
        string categoryKey,
        IReadOnlyList<string>? annotationUniqueIds = null)
    {
        SourceElementUniqueId = sourceElementUniqueId?.Trim() ?? string.Empty;
        SourceElementId = sourceElementId;
        CategoryKey = OpeningViewCategoryKeys.Normalize(categoryKey);
        AnnotationUniqueIds = (annotationUniqueIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public string SourceElementUniqueId { get; }

    public long SourceElementId { get; }

    public string CategoryKey { get; }

    public IReadOnlyList<string> AnnotationUniqueIds { get; }
}
