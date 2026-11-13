using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.FinishScheduleHarness;

internal static class RoomIdentifierChecks
{
    public static void Run(Document document, ITrueBimLogger logger, Action<bool, string> check)
    {
        using TransactionGroup group = new(document, "Room identifier regression");
        group.Start();
        Room room;
        using (Transaction transaction = new(document, "Room identifier fixture"))
        {
            transaction.Start();
            room = document.Create.NewRoom(document.Phases.Cast<Phase>().Last());
            room.Number = "17";
            room.get_Parameter(BuiltInParameter.ROOM_NAME).Set("Спальня");
            room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT).Set("Зона А");
            transaction.Commit();
        }

        check(room.Name == "Спальня 17", "Revit Room.Name reproduces the unwanted number suffix");
        ParameterReference department = ParameterReference.BuiltIn("Department",
            (int)BuiltInParameter.ROOM_DEPARTMENT, ParameterBindingKind.Instance, ParameterStorageKind.String);
        foreach (RoomIdentifierMode mode in new[]
                 { RoomIdentifierMode.Name, RoomIdentifierMode.Number, RoomIdentifierMode.CustomParameter })
        {
            string expected = mode == RoomIdentifierMode.Name ? "Спальня"
                : mode == RoomIdentifierMode.Number ? "17" : "Зона А";
            VerifyOutputs(document, room, mode, department, expected, logger, check);
        }

        using (Transaction transaction = new(document, "Name with an intentional numeric suffix"))
        {
            transaction.Start();
            room.get_Parameter(BuiltInParameter.ROOM_NAME).Set("Кабинет 17");
            transaction.Commit();
        }
        VerifyOutputs(document, room, RoomIdentifierMode.Name, department, "Кабинет 17", logger, check);
        group.RollBack();
    }

    private static void VerifyOutputs(Document document, Room room, RoomIdentifierMode mode,
        ParameterReference department, string expected, ITrueBimLogger logger, Action<bool, string> check)
    {
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault();
        ParameterReference ownershipParameter = ParameterReference.BuiltIn("Comments",
            (int)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, ParameterBindingKind.Instance, ParameterStorageKind.String);
        FinishScheduleSettings settings = defaults with
        {
            RoomIdentifier = new RoomIdentifierSettings(mode, mode == RoomIdentifierMode.CustomParameter ? department : null),
            Walls = defaults.Walls with { OwnershipParameter = ownershipParameter },
            Floors = defaults.Floors with { OwnershipParameter = ownershipParameter },
            Ceilings = defaults.Ceilings with { OwnershipParameter = ownershipParameter }
        };
        long roomId = RevitElementIds.GetValue(room.Id);
        FinishRoomCandidateSnapshot collected = new FinishElementCollector(logger).Collect(document, settings)
            .Rooms.Single(candidate => candidate.ElementId == roomId);
        check(collected.Name == room.get_Parameter(BuiltInParameter.ROOM_NAME).AsString(),
            $"Collector reads the separate room name for {mode}: {expected}");
        FinishClassifiedElement[] elements =
        [
            new(new FinishElementCandidateSnapshot(1, 10, FinishPhysicalCategory.Wall, null), FinishPreviewCategory.Walls),
            new(new FinishElementCandidateSnapshot(2, 10, FinishPhysicalCategory.Floor, null), FinishPreviewCategory.Floors),
            new(new FinishElementCandidateSnapshot(3, 10, FinishPhysicalCategory.Ceiling, null), FinishPreviewCategory.Ceilings)
        ];
        FinishQuantityResult quantities = new(elements.Select(element =>
            new FinishOccurrence(roomId, element.Element.ElementId, element.Category, 5,
                FinishQuantityMethod.RoomBoundarySubface)), []);
        RoomFinishSnapshotBuildResult snapshots = new RoomFinishSnapshotBuilder(new FinishDescriptionNormalizer())
            .Build(new RoomFinishSnapshotRequest(settings, [collected], elements,
                new Dictionary<long, FinishTypeSnapshot>(), quantities));
        FinishAggregationResult aggregation = new FinishAggregationService(
            new FinishGroupKeyBuilder(), new FinishAggregationFormatter()).Aggregate(snapshots);
        check(aggregation.RoomOutputs[roomId].RoomList == expected,
            $"Schedule room list uses {mode}: {expected}");
        FinishParameterTargetBuildResult ownership = new FinishOwnershipValueBuilder()
            .Build(settings, elements, quantities, snapshots);
        check(ownership.Targets.Count == 3 && ownership.Targets.All(target => target.Value == expected),
            $"Wall, floor and ceiling ownership use {mode}: {expected}");
        FinishQuantityResult stored = new StoredFinishQuantityCalculator().Calculate(
            [new StoredFinishElement(1, FinishPreviewCategory.Walls, collected.LevelId, expected, 5)],
            [collected], settings.RoomIdentifier);
        check(stored.Occurrences.Count == 1 && stored.Occurrences[0].RoomId == roomId && stored.Warnings.Count == 0,
            $"Stored ownership resolves the room using {mode}: {expected}");
    }
}
