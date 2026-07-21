using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishSchedulePreviewBuilder
{
    private readonly RoomScopeService roomScopeService;
    private readonly FinishClassificationService classificationService;

    public FinishSchedulePreviewBuilder(
        RoomScopeService roomScopeService,
        FinishClassificationService classificationService)
    {
        this.roomScopeService = roomScopeService ?? throw new ArgumentNullException(nameof(roomScopeService));
        this.classificationService = classificationService
            ?? throw new ArgumentNullException(nameof(classificationService));
    }

    public FinishSchedulePreviewResult Build(
        FinishElementCollection collection,
        FinishScheduleSettings settings)
    {
        return BuildDetailed(collection, settings).Preview;
    }

    public FinishSchedulePreviewBuild BuildDetailed(
        FinishElementCollection collection,
        FinishScheduleSettings settings)
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        FinishRoomScopeResult roomScope = roomScopeService.Select(collection.Rooms, settings.Scope);
        FinishClassificationResult classification = classificationService.Classify(collection, settings);
        FinishBoundingBoxIndex index = new(classification.Elements);

        Dictionary<FinishPreviewCategory, HashSet<long>> inScopeIds = new()
        {
            [FinishPreviewCategory.Walls] = [],
            [FinishPreviewCategory.Floors] = [],
            [FinishPreviewCategory.Ceilings] = []
        };
        int potentialPairs = 0;
        FinishPreviewCategory[] categories =
        [
            FinishPreviewCategory.Walls,
            FinishPreviewCategory.Floors,
            FinishPreviewCategory.Ceilings
        ];
        foreach (FinishRoomCandidateSnapshot room in roomScope.SelectedRooms)
        {
            foreach (FinishPreviewCategory category in categories)
            {
                AxisAlignedBox3D searchBounds = FinishCandidateSearchRules.CreateSearchBounds(
                    room.Bounds!,
                    category);
                foreach (FinishClassifiedElement element in index.Query(searchBounds)
                             .Where(element => element.Category == category))
                {
                    potentialPairs++;
                    inScopeIds[category].Add(element.Element.ElementId);
                }
            }
        }

        FinishPreviewCategoryCounts walls = CreateCounts(
            collection.Walls.Count,
            FinishPreviewCategory.Walls,
            classification,
            inScopeIds);
        FinishPreviewCategoryCounts floors = CreateCounts(
            collection.Floors.Count,
            FinishPreviewCategory.Floors,
            classification,
            inScopeIds);
        FinishPreviewCategoryCounts ceilings = CreateCounts(
            collection.Floors.Count + collection.Ceilings.Count,
            FinishPreviewCategory.Ceilings,
            classification,
            inScopeIds);

        List<string> warnings = BuildWarnings(roomScope, classification, index);
        FinishClassifiedElement[] inScopeElements = classification.Elements
            .Where(element => inScopeIds[element.Category].Contains(element.Element.ElementId))
            .OrderBy(element => element.Element.ElementId)
            .ThenBy(element => element.Category)
            .ToArray();
        FinishSchedulePreviewResult preview = new(
            collection.Rooms.Count,
            roomScope,
            walls,
            floors,
            ceilings,
            new FinishPreviewIndexCounts(
                index.IndexedElementCount,
                index.ElementsWithoutBounds,
                potentialPairs),
            warnings);
        return new FinishSchedulePreviewBuild(
            preview,
            roomScope,
            classification,
            inScopeElements);
    }

    private static FinishPreviewCategoryCounts CreateCounts(
        int sourceCollected,
        FinishPreviewCategory category,
        FinishClassificationResult classification,
        IReadOnlyDictionary<FinishPreviewCategory, HashSet<long>> inScopeIds)
    {
        return new FinishPreviewCategoryCounts(
            sourceCollected,
            classification.Elements.Count(element => element.Category == category),
            inScopeIds[category].Count);
    }

    private static List<string> BuildWarnings(
        FinishRoomScopeResult roomScope,
        FinishClassificationResult classification,
        FinishBoundingBoxIndex index)
    {
        List<string> warnings = [];
        if (roomScope.InvalidRooms.Count > 0)
        {
            warnings.Add($"Невалидных помещений пропущено: {roomScope.InvalidRooms.Count}.");
        }

        if (roomScope.MissingScopeValueCount > 0)
        {
            warnings.Add($"Без значения параметра scope: {roomScope.MissingScopeValueCount}.");
        }

        int configurationSkips = classification.SkippedElements.Count(skip =>
            skip.Reason != FinishClassificationSkipReason.ValueDoesNotMatch);
        if (configurationSkips > 0)
        {
            warnings.Add($"Элементов с отсутствующей или неоднозначной классификацией: {configurationSkips}.");
        }

        if (index.ElementsWithoutBounds > 0)
        {
            warnings.Add($"Классифицированных элементов без bounding box: {index.ElementsWithoutBounds}.");
        }

        return warnings;
    }
}
