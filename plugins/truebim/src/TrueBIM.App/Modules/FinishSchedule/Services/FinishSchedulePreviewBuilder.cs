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
        FinishRoomSearchBounds searchBounds = new(collection.Rooms, classification.Elements);

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
                AxisAlignedBox3D categoryBounds = searchBounds.Create(room, category);
                foreach (FinishClassifiedElement element in index.Query(categoryBounds)
                             .Where(element => element.Category == category && searchBounds.Includes(room, element)))
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

        List<string> warnings = BuildWarnings(roomScope, classification, index, collection, settings.Scope);
        FinishClassifiedElement[] inScopeElements = classification.Elements
            .Where(element => inScopeIds[element.Category].Contains(element.Element.ElementId))
            .OrderBy(element => element.Element.ElementId)
            .ThenBy(element => element.Category)
            .ToArray();
        FinishSchedulePreviewResult preview = new(
            roomScope.SelectedRooms.Count + roomScope.InvalidRooms.Count + roomScope.OutsideScopeCount,
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
        FinishBoundingBoxIndex index,
        FinishElementCollection collection,
        ReportScopeSettings scope)
    {
        List<string> warnings = [];
        if (roomScope.InvalidRooms.Count > 0)
        {
            warnings.Add($"Пропущено размещённых помещений без расчётной площади или габаритов: {roomScope.InvalidRooms.Count}.");
        }

        if (roomScope.MissingScopeValueCount > 0)
        {
            warnings.Add($"Без значения параметра scope: {roomScope.MissingScopeValueCount}.");
        }

        var levelElements = scope.Kind == ReportScopeKind.Level
            ? collection.Walls.Concat(collection.Floors).Concat(collection.Ceilings)
                .Where(element => element.LevelId == scope.LevelId).Select(element => element.ElementId).ToHashSet()
            : null;
        foreach (var group in classification.SkippedElements
                     .Where(skip => skip.Reason is FinishClassificationSkipReason.MissingType
                         or FinishClassificationSkipReason.AmbiguousFloorClassification)
                     .GroupBy(skip => skip.Reason))
        {
            string reason = group.Key switch
            {
                FinishClassificationSkipReason.MissingType =>
                    "Revit не вернул тип. Проверьте тип элемента в модели.",
                FinishClassificationSkipReason.AmbiguousFloorClassification =>
                    "Одно значение типа в настройках обозначает и пол, и потолок. Укажите разные значения и повторите расчёт.",
                _ => throw new ArgumentOutOfRangeException(nameof(group.Key))
            };
            long[] ids = group.Select(skip => skip.ElementId).OrderBy(id => id).ToArray();
            string level = levelElements is null ? "" : $" К выбранному уровню привязано: {ids.Count(levelElements.Contains)}.";
            warnings.Add($"Проверка типов по всему проекту. Пропущено элементов: {ids.Length} (ID {string.Join(", ", ids)}).{level} {reason}");
        }

        if (index.ElementsWithoutBounds > 0)
        {
            warnings.Add($"Классифицированных элементов без bounding box: {index.ElementsWithoutBounds}.");
        }

        return warnings;
    }
}
