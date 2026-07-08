using Autodesk.Revit.DB;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Services;

public sealed class AutoTagExistingTagIndex
{
    private readonly Dictionary<long, int> tagCountsByElementId;

    private AutoTagExistingTagIndex(Dictionary<long, int> tagCountsByElementId)
    {
        this.tagCountsByElementId = tagCountsByElementId;
    }

    public static AutoTagExistingTagIndex Create(Document document, View activeView, ITrueBimLogger? logger = null)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));

        Dictionary<long, int> counts = [];
        try
        {
            IEnumerable<IndependentTag> tags = new FilteredElementCollector(document, activeView.Id)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (IndependentTag tag in tags)
            {
                try
                {
#if REVIT2022_OR_GREATER
                    foreach (ElementId elementId in tag.GetTaggedLocalElementIds())
                    {
                        AddTaggedElement(counts, elementId);
                    }
#else
                    AddTaggedElement(counts, tag.TaggedLocalElementId);
#endif
                }
                catch (Exception exception)
                {
                    logger?.Warning($"Failed to read tagged element ids for tag {RevitElementIds.GetValue(tag.Id)}: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            logger?.Warning($"Failed to collect existing tags for view '{activeView.Name}': {exception.Message}");
        }

        return new AutoTagExistingTagIndex(counts);
    }

    private static void AddTaggedElement(Dictionary<long, int> counts, ElementId elementId)
    {
        if (elementId == ElementId.InvalidElementId)
        {
            return;
        }

        long id = RevitElementIds.GetValue(elementId);
        counts[id] = counts.TryGetValue(id, out int count) ? count + 1 : 1;
    }

    public int GetTagCount(ElementId elementId)
    {
        return tagCountsByElementId.TryGetValue(RevitElementIds.GetValue(elementId), out int count)
            ? count
            : 0;
    }
}
