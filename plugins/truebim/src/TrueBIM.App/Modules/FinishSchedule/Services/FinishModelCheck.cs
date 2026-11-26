using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed record FinishModelCheck(long ElementId, bool NeedsCorrection, string Text)
{
    public static IReadOnlyList<FinishModelCheck> Build(FinishSchedulePreviewResult preview)
    {
        var rooms = preview.RoomScope.SelectedRooms.ToDictionary(r => r.ElementId);
        string Room(long id) => rooms.TryGetValue(id, out var room) ? $"{room.Number} (ID {id})" : $"ID {id}";
        return preview.GeometryWarnings.Where(w => w.ElementId.HasValue &&
                (w.Code == FinishGeometryWarningCode.MultipleRoomContacts || FinishGeometryWarningClassifier.AffectsScheduleValue(w)))
            .GroupBy(w => w.ElementId!.Value).Select(group =>
            {
                var conflict = group.FirstOrDefault(w => w.Code == FinishGeometryWarningCode.MultipleRoomContacts);
                bool correction = group.Any(FinishGeometryWarningClassifier.AffectsScheduleValue);
                var reason = correction ? string.Join(" ", group.Where(FinishGeometryWarningClassifier.AffectsScheduleValue)
                    .Select(w => w.StoredOwnershipIssue ?? w.Message).Distinct()) : "";
                if (conflict?.RoomId is long owner)
                {
                    var compared = string.Join(", ", conflict.CompetingContacts.Select(c => $"{Room(c.RoomId)} — {c.AreaSquareMeters:0.###} м²"));
                    reason += $" Учтён в {Room(owner)}. Контакты: {compared}.";
                    if (conflict.SmallWallContactResolved) reason += " Малое касание стены разрешено автоматически.";
                }
                return new FinishModelCheck(group.Key, correction, $"ID {group.Key} · {reason.Trim()}");
            }).OrderByDescending(row => row.NeedsCorrection).ThenBy(row => row.ElementId).ToArray();
    }
}
