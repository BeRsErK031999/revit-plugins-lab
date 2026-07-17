using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class RoomFinishSnapshotBuilder
{
    private readonly FinishDescriptionNormalizer normalizer;

    public RoomFinishSnapshotBuilder(FinishDescriptionNormalizer normalizer)
    {
        this.normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
    }

    public RoomFinishSnapshotBuildResult Build(RoomFinishSnapshotRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        List<RoomFinishSnapshot> snapshots = [];
        List<FinishAggregationWarning> warnings = [];
        foreach (FinishRoomCandidateSnapshot room in request.Rooms)
        {
            string identifier = ResolveIdentifier(room, request.Settings.RoomIdentifier, warnings);
            snapshots.Add(new RoomFinishSnapshot(
                room.ElementId,
                identifier,
                BuildCategory(room.ElementId, FinishPreviewCategory.Walls, request.Settings.Walls.IsEnabled, request, warnings),
                BuildCategory(room.ElementId, FinishPreviewCategory.Floors, request.Settings.Floors.IsEnabled, request, warnings),
                BuildCategory(room.ElementId, FinishPreviewCategory.Ceilings, request.Settings.Ceilings.IsEnabled, request, warnings)));
        }

        return new RoomFinishSnapshotBuildResult(snapshots, warnings);
    }

    private RoomFinishCategorySnapshot BuildCategory(
        long roomId,
        FinishPreviewCategory category,
        bool isEnabled,
        RoomFinishSnapshotRequest request,
        List<FinishAggregationWarning> warnings)
    {
        if (!isEnabled)
        {
            return new RoomFinishCategorySnapshot(false, FinishValueState.NoFinish, []);
        }

        FinishOccurrence[] occurrences = request.Quantities.Occurrences
            .Where(occurrence => occurrence.RoomId == roomId && occurrence.Category == category)
            .ToArray();
        bool hasUnknownQuantity = request.Quantities.Warnings.Any(warning =>
            warning.RoomId == roomId
            && (!warning.Category.HasValue || warning.Category == category));
        Dictionary<string, List<DescribedOccurrence>> described = new(StringComparer.Ordinal);
        foreach (FinishOccurrence occurrence in occurrences)
        {
            string? sourceDescription = ResolveDescription(occurrence.ElementId, request);
            bool isMissing = string.IsNullOrWhiteSpace(sourceDescription);
            NormalizedFinishDescription normalized = normalizer.Normalize(sourceDescription);
            if (isMissing)
            {
                warnings.Add(new FinishAggregationWarning(
                    FinishAggregationWarningCode.MissingDescription,
                    $"У элемента {occurrence.ElementId} не задано описание отделки; использован маркер «{FinishDescriptionNormalizer.MissingDescriptionDisplay}».",
                    roomId,
                    occurrence.ElementId,
                    category));
            }

            if (!described.TryGetValue(normalized.ComparisonKey, out List<DescribedOccurrence>? values))
            {
                values = [];
                described.Add(normalized.ComparisonKey, values);
            }

            values.Add(new DescribedOccurrence(normalized.DisplayValue, occurrence.AreaSquareMeters));
        }

        RoomFinishItem[] items = described
            .Select(pair => new RoomFinishItem(
                new NormalizedFinishDescription(
                    pair.Value.Select(value => value.DisplayValue)
                        .OrderBy(value => value, NaturalStringComparer.Instance)
                        .First(),
                    pair.Key),
                pair.Value.Sum(value => value.AreaSquareMeters)))
            .OrderBy(item => item.Description.DisplayValue, NaturalStringComparer.Instance)
            .ToArray();
        FinishValueState state = hasUnknownQuantity
            ? FinishValueState.Unknown
            : items.Length == 0
                ? FinishValueState.NoFinish
                : FinishValueState.Resolved;
        if (hasUnknownQuantity)
        {
            warnings.Add(new FinishAggregationWarning(
                FinishAggregationWarningCode.UnknownQuantity,
                $"Для помещения {roomId} категория «{GetCategoryName(category)}» рассчитана не полностью.",
                roomId,
                Category: category));
        }

        return new RoomFinishCategorySnapshot(true, state, items);
    }

    private static string? ResolveDescription(
        long elementId,
        RoomFinishSnapshotRequest request)
    {
        if (!request.Elements.TryGetValue(elementId, out FinishClassifiedElement? element)
            || !request.Types.TryGetValue(element.Element.TypeId, out FinishTypeSnapshot? type)
            || !type.HasDescriptionParameter
            || type.DescriptionValue is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(type.DescriptionValue.RawValue)
            ? type.DescriptionValue.RawValue
            : type.DescriptionValue.DisplayValue;
    }

    private static string ResolveIdentifier(
        FinishRoomCandidateSnapshot room,
        RoomIdentifierSettings settings,
        List<FinishAggregationWarning> warnings)
    {
        string? value = settings.Mode switch
        {
            RoomIdentifierMode.Number => room.Number,
            RoomIdentifierMode.Name => room.Name,
            RoomIdentifierMode.CustomParameter => ReadCustomIdentifier(room, settings.CustomParameter),
            _ => null
        };
        string normalized = FinishDescriptionNormalizer.CollapseWhitespace(value);
        if (normalized.Length > 0)
        {
            return normalized;
        }

        string fallback = $"#{room.ElementId}";
        warnings.Add(new FinishAggregationWarning(
            FinishAggregationWarningCode.MissingRoomIdentifier,
            $"У помещения {room.ElementId} отсутствует выбранный идентификатор; использован «{fallback}».",
            room.ElementId));
        return fallback;
    }

    private static string? ReadCustomIdentifier(
        FinishRoomCandidateSnapshot room,
        ParameterReference? reference)
    {
        if (reference is null
            || !room.TryGetParameterValue(reference, out FinishParameterValueSnapshot? value)
            || value is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(value.RawValue)
            ? value.RawValue
            : value.DisplayValue;
    }

    private static string GetCategoryName(FinishPreviewCategory category)
    {
        return category switch
        {
            FinishPreviewCategory.Walls => "Стены",
            FinishPreviewCategory.Floors => "Полы",
            FinishPreviewCategory.Ceilings => "Потолки",
            _ => category.ToString()
        };
    }

    private sealed record DescribedOccurrence(string DisplayValue, double AreaSquareMeters);
}
