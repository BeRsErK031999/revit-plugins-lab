using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class RoomScopeService
{
    private const double MinimumRoomArea = 1e-9;

    public FinishRoomScopeResult Select(
        IEnumerable<FinishRoomCandidateSnapshot> rooms,
        ReportScopeSettings scope)
    {
        if (rooms is null)
        {
            throw new ArgumentNullException(nameof(rooms));
        }

        if (scope is null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        List<FinishRoomCandidateSnapshot> selected = [];
        List<FinishRoomScopeSkip> invalid = [];
        int outsideScopeCount = 0;
        int missingScopeValueCount = 0;

        foreach (FinishRoomCandidateSnapshot room in rooms.OrderBy(room => room.ElementId))
        {
            FinishRoomSkipReason? invalidReason = GetInvalidReason(room);
            if (invalidReason.HasValue)
            {
                invalid.Add(new FinishRoomScopeSkip(room.ElementId, invalidReason.Value));
                continue;
            }

            ScopeMatch match = MatchesScope(room, scope);
            if (match == ScopeMatch.Match)
            {
                selected.Add(room);
            }
            else
            {
                outsideScopeCount++;
                if (match == ScopeMatch.MissingValue)
                {
                    missingScopeValueCount++;
                }
            }
        }

        return new FinishRoomScopeResult(
            selected,
            invalid,
            outsideScopeCount,
            missingScopeValueCount);
    }

    private static FinishRoomSkipReason? GetInvalidReason(FinishRoomCandidateSnapshot room)
    {
        if (!room.HasLocation)
        {
            return FinishRoomSkipReason.Unplaced;
        }

        if (room.Area <= MinimumRoomArea || double.IsNaN(room.Area) || double.IsInfinity(room.Area))
        {
            return FinishRoomSkipReason.NotEnclosed;
        }

        return room.Bounds is null ? FinishRoomSkipReason.MissingBounds : null;
    }

    private static ScopeMatch MatchesScope(
        FinishRoomCandidateSnapshot room,
        ReportScopeSettings scope)
    {
        return scope.Kind switch
        {
            ReportScopeKind.EntireProject => ScopeMatch.Match,
            ReportScopeKind.Level => scope.LevelId.HasValue && room.LevelId == scope.LevelId.Value
                ? ScopeMatch.Match
                : ScopeMatch.NoMatch,
            ReportScopeKind.Section => MatchesSection(room, scope),
            _ => ScopeMatch.NoMatch
        };
    }

    private static ScopeMatch MatchesSection(
        FinishRoomCandidateSnapshot room,
        ReportScopeSettings scope)
    {
        if (scope.SectionParameter is null
            || !room.TryGetParameterValue(scope.SectionParameter, out FinishParameterValueSnapshot? value)
            || value is null
            || string.IsNullOrWhiteSpace(value.RawValue) && string.IsNullOrWhiteSpace(value.DisplayValue))
        {
            return ScopeMatch.MissingValue;
        }

        return value.Matches(scope.SectionValue) ? ScopeMatch.Match : ScopeMatch.NoMatch;
    }

    private enum ScopeMatch
    {
        Match,
        NoMatch,
        MissingValue
    }
}
