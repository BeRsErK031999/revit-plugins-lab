using Autodesk.Revit.DB;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.FinishSchedule.Revit;

/// <summary>Продолжает подтверждённую принадлежность по соединённым участкам одного контура отделки.</summary>
internal sealed class FinishConnectedWallContactSource
{
    public void AddContacts(Document document, FinishQuantityRequest request, FinishOccurrenceAccumulator accumulator)
    {
        Dictionary<long, long> owners = accumulator.Build().Where(item => item.Category == FinishPreviewCategory.Walls)
            .GroupBy(item => item.ElementId).ToDictionary(group => group.Key,
                group => group.OrderByDescending(item => item.AreaSquareMeters).ThenBy(item => item.RoomId).First().RoomId);
        List<WallPart> walls = [];
        foreach (FinishClassifiedElement item in request.Elements.Where(item => item.Category == FinishPreviewCategory.Walls))
        {
            if (item.Element.Bounds is null || document.GetElement(RevitElementIds.Create(item.Element.ElementId)) is not Wall wall
                || wall.Location is not LocationCurve location) continue;
            Curve curve = location.Curve;
            Curve plan = curve.CreateTransformed(Transform.CreateTranslation(new XYZ(0, 0, -curve.GetEndPoint(0).Z)));
            walls.Add(new WallPart(item.Element.ElementId, wall.LevelId, wall.GetTypeId(), wall.Width, item.Element.Bounds, plan));
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
            foreach (WallPart part in component)
                accumulator.KeepLargest(new FinishOccurrence(adjacentOwners[0], part.Id, FinishPreviewCategory.Walls,
                    1, FinishQuantityMethod.ConnectedWallContour));
        }
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
