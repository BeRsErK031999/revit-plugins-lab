using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishBoundingBoxIndex
{
    private readonly IReadOnlyList<FinishClassifiedElement> elementsByMinX;

    public FinishBoundingBoxIndex(IEnumerable<FinishClassifiedElement> elements)
    {
        FinishClassifiedElement[] all = (elements ?? throw new ArgumentNullException(nameof(elements)))
            .ToArray();
        elementsByMinX = all
            .Where(element => element.Element.Bounds is not null)
            .OrderBy(element => element.Element.Bounds!.MinX)
            .ThenBy(element => element.Element.ElementId)
            .ToArray();
        ElementsWithoutBounds = all.Length - elementsByMinX.Count;
    }

    public int IndexedElementCount => elementsByMinX.Count;

    public int ElementsWithoutBounds { get; }

    public IReadOnlyList<FinishClassifiedElement> Query(
        AxisAlignedBox3D bounds,
        double tolerance = 0)
    {
        if (bounds is null)
        {
            throw new ArgumentNullException(nameof(bounds));
        }

        List<FinishClassifiedElement> result = [];
        foreach (FinishClassifiedElement element in elementsByMinX)
        {
            AxisAlignedBox3D elementBounds = element.Element.Bounds!;
            if (elementBounds.MinX > bounds.MaxX + tolerance)
            {
                break;
            }

            if (elementBounds.Intersects(bounds, tolerance))
            {
                result.Add(element);
            }
        }

        return result;
    }
}
