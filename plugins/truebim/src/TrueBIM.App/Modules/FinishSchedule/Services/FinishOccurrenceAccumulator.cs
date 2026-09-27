using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishOccurrenceAccumulator
{
    private readonly Dictionary<OccurrenceKey, AccumulatedOccurrence> occurrences = [];

    public void Add(
        long roomId,
        long elementId,
        FinishPreviewCategory category,
        double areaSquareMeters,
        FinishQuantityMethod method)
    {
        FinishOccurrence occurrence = new(
            roomId,
            elementId,
            category,
            areaSquareMeters,
            method);
        OccurrenceKey key = new(roomId, elementId, category);
        if (!occurrences.TryGetValue(key, out AccumulatedOccurrence? existing))
        {
            occurrences.Add(key, new AccumulatedOccurrence(
                occurrence.AreaSquareMeters,
                occurrence.Method));
            return;
        }

        if (existing.Method != method)
        {
            throw new InvalidOperationException(
                $"Finish occurrence {roomId}/{elementId}/{category} cannot mix quantity methods.");
        }

        existing.AreaSquareMeters += occurrence.AreaSquareMeters;
    }

    public IReadOnlyList<FinishOccurrence> Build()
    {
        return occurrences
            .OrderBy(pair => pair.Key.RoomId)
            .ThenBy(pair => pair.Key.Category)
            .ThenBy(pair => pair.Key.ElementId)
            .Select(pair => new FinishOccurrence(
                pair.Key.RoomId,
                pair.Key.ElementId,
                pair.Key.Category,
                pair.Value.AreaSquareMeters,
                pair.Value.Method))
            .ToArray();
    }

    public void KeepLargest(FinishOccurrence occurrence)
    {
        OccurrenceKey key = new(occurrence.RoomId, occurrence.ElementId, occurrence.Category);
        if (!occurrences.TryGetValue(key, out AccumulatedOccurrence? existing)
            || occurrence.AreaSquareMeters > existing.AreaSquareMeters)
        {
            occurrences[key] = new AccumulatedOccurrence(occurrence.AreaSquareMeters, occurrence.Method);
        }
    }

    private sealed record OccurrenceKey(
        long RoomId,
        long ElementId,
        FinishPreviewCategory Category);

    private sealed class AccumulatedOccurrence
    {
        public AccumulatedOccurrence(double areaSquareMeters, FinishQuantityMethod method)
        {
            AreaSquareMeters = areaSquareMeters;
            Method = method;
        }

        public double AreaSquareMeters { get; set; }

        public FinishQuantityMethod Method { get; }
    }
}
