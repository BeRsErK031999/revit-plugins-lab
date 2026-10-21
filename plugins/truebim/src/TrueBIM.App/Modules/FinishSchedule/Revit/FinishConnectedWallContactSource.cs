using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Продолжает подтверждённую принадлежность по соединённым участкам одного контура отделки.</summary>
internal sealed class FinishConnectedWallContactSource
{
    public IReadOnlyDictionary<long, long> AddContacts(Document document, FinishQuantityRequest request, FinishOccurrenceAccumulator accumulator)
    {
        Dictionary<long, FinishOccurrence[]> contacts = accumulator.Build().Where(item => item.Category == FinishPreviewCategory.Walls)
            .GroupBy(item => item.ElementId).ToDictionary(group => group.Key,
                group => group.OrderByDescending(item => item.AreaSquareMeters).ThenBy(item => item.RoomId).ToArray());
        Dictionary<long, long> owners = [];
        Dictionary<long, HashSet<long>> ambiguous = [];
        Dictionary<long, long> contourOwners = [];
        List<WallPart> walls = [];
        foreach (FinishClassifiedElement item in request.Elements.Where(item => item.Category == FinishPreviewCategory.Walls))
        {
            if (item.Element.Bounds is null || document.GetElement(RevitElementIds.Create(item.Element.ElementId)) is not Wall wall
                || wall.Location is not LocationCurve location) continue;
            Curve curve = location.Curve;
            Curve plan = curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, -curve.GetEndPoint(0).Z)));
            walls.Add(new WallPart(item.Element.ElementId, wall.LevelId, wall.GetTypeId(), wall.Width, item.Element.Bounds, plan));
            if (!contacts.TryGetValue(item.Element.ElementId, out FinishOccurrence[]? candidates)) continue;
            // Mitred joins shorten one face by up to one wall thickness. Such a difference
            // is not evidence that the opposite room owns the finish: consult its connected contour.
            double jointArea = RevitAreaUnits.ToSquareMeters(wall.Width * (item.Element.Bounds.MaxZ - item.Element.Bounds.MinZ));
            HashSet<long> nearOwners = new(candidates
                .Where(contact => candidates[0].AreaSquareMeters - contact.AreaSquareMeters <= jointArea + 1e-6)
                .Select(contact => contact.RoomId));
            if (nearOwners.Count > 1) ambiguous[item.Element.ElementId] = nearOwners;
            else owners[item.Element.ElementId] = candidates[0].RoomId;
        }
        Dictionary<long, WallPart> pending = walls.Where(wall => !owners.ContainsKey(wall.Id)).ToDictionary(wall => wall.Id);
        WallPart[] anchors = walls.Where(wall => owners.ContainsKey(wall.Id)).ToArray();
        while (pending.Count > 0)
        {
            List<WallPart> component = [pending.First().Value];
            pending.Remove(component[0].Id);
            for (int index = 0; index < component.Count; index++)
            {
                WallPart[] connected = pending.Values.Where(other => Touch(component[index], other)).ToArray();
                component.AddRange(connected);
                foreach (WallPart other in connected) pending.Remove(other.Id);
            }
            long[] adjacentOwners = anchors.Where(anchor => component.Any(part => Touch(part, anchor)))
                .Select(anchor => owners[anchor.Id]).Distinct().ToArray();
            // A junction between different rooms is genuinely ambiguous; keep it unresolved.
            if (adjacentOwners.Length != 1) continue;
            long owner = adjacentOwners[0];
            if (component.Any(part => ambiguous.TryGetValue(part.Id, out HashSet<long>? candidates)
                    && !candidates.Contains(owner))) continue;
            foreach (WallPart part in component)
            {
                if (ambiguous.ContainsKey(part.Id)) contourOwners[part.Id] = owner;
                else accumulator.KeepLargest(new FinishOccurrence(owner, part.Id, FinishPreviewCategory.Walls,
                    1, FinishQuantityMethod.ConnectedWallContour));
            }
        }
        return contourOwners;
    }

    private static bool Touch(WallPart first, WallPart second)
    {
        if (first.LevelId != second.LevelId || first.TypeId != second.TypeId
            || !first.Bounds.Intersects(second.Bounds, FinishCandidateSearchRules.HorizontalToleranceInternal)) return false;
        double tolerance = (first.Width + second.Width) / 2 + FinishCandidateSearchRules.HorizontalToleranceInternal;
        return Enumerable.Range(0, 2).Any(index => first.Plan.Distance(second.Plan.GetEndPoint(index)) <= tolerance
            || second.Plan.Distance(first.Plan.GetEndPoint(index)) <= tolerance);
    }

    private sealed record WallPart(long Id, ElementId LevelId, ElementId TypeId, double Width, AxisAlignedBox3D Bounds, Curve Plan);
}
