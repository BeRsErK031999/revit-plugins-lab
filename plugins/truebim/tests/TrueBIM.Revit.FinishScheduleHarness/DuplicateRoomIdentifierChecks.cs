using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.FinishScheduleHarness;

internal static class DuplicateRoomIdentifierChecks
{
    public static void Run(Document document, ITrueBimLogger logger, Action<bool, string> check)
    {
        using TransactionGroup group = new(document, "Repeated room identifiers regression");
        group.Start();
        new FinishScheduleDefaultParameterService(logger).CreateOrUpdate(document.Application, document);
        List<Room> rooms = [];
        List<Element> finishes = [];
        using (Transaction fixture = new(document, "Three rooms with repeated identifiers"))
        {
            fixture.Start();
            Level level = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(item => item.Elevation).First();
            WallType baseWall = new FilteredElementCollector(document).OfClass(typeof(WallType)).Cast<WallType>()
                .First(type => type.Kind == WallKind.Basic);
            FloorType floorType = (FloorType)new FilteredElementCollector(document).OfClass(typeof(FloorType))
                .Cast<FloorType>().First(type => !type.IsFoundationSlab).Duplicate("Identifier floor");
            CeilingType ceilingType = (CeilingType)new FilteredElementCollector(document).OfClass(typeof(CeilingType))
                .Cast<CeilingType>().First().Duplicate("Identifier ceiling");
            ConfigureType(floorType, "Пол", "Floor finish");
            ConfigureType(ceilingType, "Потолки", "Ceiling finish");
            for (int index = 0; index < 3; index++)
            {
                WallType wallType = (WallType)baseWall.Duplicate("Identifier wall " + index);
                ConfigureType(wallType, "Внутренняя отделка", index == 0 ? "Paint" : index == 1 ? "Tile" : "Plaster");
                double x = index * 20;
                XYZ[] points = [new(x, 0, level.Elevation), new(x + 10, 0, level.Elevation),
                    new(x + 10, 10, level.Elevation), new(x, 10, level.Elevation)];
                CurveLoop outline = new();
                for (int edge = 0; edge < 4; edge++)
                {
                    Line line = Line.CreateBound(points[edge], points[(edge + 1) % 4]);
                    outline.Append(line);
                    finishes.Add(Wall.Create(document, line, wallType.Id, level.Id, 10, 0, false, false));
                }
                finishes.Add(Floor.Create(document, [outline], floorType.Id, level.Id));
                Ceiling ceiling = Ceiling.Create(document, [outline], ceilingType.Id, level.Id);
                ceiling.get_Parameter(BuiltInParameter.CEILING_HEIGHTABOVELEVEL_PARAM).Set(10);
                finishes.Add(ceiling);
                document.Regenerate();
                Room room = document.Create.NewRoom(level, new UV(x + 5, 5));
                room.Number = index < 2 ? "16" : "18";
                room.get_Parameter(BuiltInParameter.ROOM_NAME).Set(index < 2 ? "Помещение" : "Спальня");
                room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET).Set(10);
                room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT).Set(index < 2 ? "Общая зона" : "Спальная зона");
                room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Section " + index);
                foreach (Element finish in finishes.Skip(index * 6))
                {
                    finish.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("Section " + index);
                }
                rooms.Add(room);
            }
            fixture.Commit();
        }
        check(rooms.All(room => room.Area > 0) && finishes.Count == 18,
            "Duplicate identifier fixture has three placed rooms and eighteen finish elements");
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault() with
        {
            DescriptionParameter = ParameterReference.BuiltIn("Description", (long)BuiltInParameter.ALL_MODEL_DESCRIPTION,
                ParameterBindingKind.Type, ParameterStorageKind.String),
            RoomIdentifier = new RoomIdentifierSettings(RoomIdentifierMode.Name, null),
            ScheduleName = "Repeated room identifiers"
        };
        FinishScheduleSettings settings = new FinishSchedulePreferredParameterResolver(
            new FinishScheduleParameterOptionService(new ParameterCatalogMatcher())).Resolve(defaults,
            new ParameterCatalogService(logger).Collect(document), ParameterCatalogService.TargetCategories);
        FinishSchedulePreviewService service = new(new FinishElementCollector(logger),
            new FinishSchedulePreviewBuilder(new RoomScopeService(), new FinishClassificationService()), logger);
        FinishScheduleWriteWorkflow workflow = new(service, new RoomFinishWriteValueBuilder(), new FinishOwnershipValueBuilder(),
            new RoomFinishParameterWriter(new FinishParameterChangePlanner()), new FinishOwnershipWriter(new FinishParameterChangePlanner()),
            new FinishRoomSchedulePlanBuilder(), new FinishRoomScheduleBuilder(new FinishScheduleMetadataService(), logger), logger);

        foreach (RoomIdentifierMode mode in new[] { RoomIdentifierMode.Name, RoomIdentifierMode.Number, RoomIdentifierMode.CustomParameter })
        {
            FinishScheduleSettings modeSettings = settings with
            {
                RoomIdentifier = new RoomIdentifierSettings(mode, mode == RoomIdentifierMode.CustomParameter
                    ? ParameterReference.BuiltIn("Department", (long)BuiltInParameter.ROOM_DEPARTMENT,
                        ParameterBindingKind.Instance, ParameterStorageKind.String) : null),
                ScheduleName = "Repeated identifiers " + mode
            };
            string identifier = mode == RoomIdentifierMode.Name ? "Помещение" : mode == RoomIdentifierMode.Number ? "16" : "Общая зона";
            string otherIdentifier = mode == RoomIdentifierMode.Name ? "Спальня" : mode == RoomIdentifierMode.Number ? "18" : "Спальная зона";
            RunMode(document, service, workflow, modeSettings, identifier, otherIdentifier, rooms, finishes,
                (passed, message) => check(passed, mode + ": " + message));
        }
        group.RollBack();
    }

    private static void RunMode(Document document, FinishSchedulePreviewService service, FinishScheduleWriteWorkflow workflow,
        FinishScheduleSettings settings, string identifier, string otherIdentifier, IReadOnlyList<Room> rooms,
        IReadOnlyList<Element> finishes, Action<bool, string> check)
    {
        VerifyCalculation(service.BuildDetailed(document, settings), identifier, rooms, finishes, check);
        FinishScheduleWriteResult applied = workflow.Apply(document, workflow.Prepare(document, settings));
        check(applied.Succeeded, "Automatic workflow applies successfully");
        check(!applied.Warnings.Any(warning => warning.Contains("неоднозначна")), "No ambiguous ownership warning");
        check(rooms.Take(2).All(room => RevitFinishParameterAccess.FindParameter(room, settings.RoomListOutputParameter!)!.AsString() == identifier),
            "Both physical rooms receive one shared room-list label");
        ViewSchedule schedule = (ViewSchedule)document.GetElement(RevitElementIds.Create(applied.Schedule!.ScheduleId));
        string text = ScheduleText(schedule);
        check(text.Contains("Paint") && text.Contains("Tile") && !text.Contains(identifier + ", " + identifier),
            "Native schedule contains both finish types without repeating the identifier");
        FinishScheduleSettings manual = settings with { WriteOwnership = false };
        FinishScheduleCalculationResult readBack = service.BuildDetailed(document, manual);
        VerifyCalculation(readBack, identifier, rooms, finishes, check);
        check(readBack.Quantities.CacheMetrics == FinishGeometryCacheMetrics.Empty,
            "Stored ownership recalculation does not request geometry");
        FinishScheduleWriteResult repeated = workflow.Apply(document, workflow.Prepare(document, settings));
        check(repeated.Succeeded && repeated.AppliedRoomValues == 0 && repeated.AppliedOwnershipValues == 0,
            "Repeating automatic calculation preserves room values and ownership");

        FinishScheduleSettings section = manual with
        {
            Scope = new ReportScopeSettings(ReportScopeKind.Section, null,
                ParameterReference.BuiltIn("Comments", (long)BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
                    ParameterBindingKind.Instance, ParameterStorageKind.String), "Section 1")
        };
        FinishScheduleCalculationResult scoped = service.BuildDetailed(document, section);
        long[] expectedIds = finishes.Skip(6).Take(6).Select(item => RevitElementIds.GetValue(item.Id)).OrderBy(id => id).ToArray();
        check(scoped.Quantities.Occurrences.Select(item => item.ElementId).OrderBy(id => id).SequenceEqual(expectedIds)
            && scoped.Quantities.Occurrences.All(item => item.RoomId == RevitElementIds.GetValue(rooms[1].Id)),
            "Section filter preserves the six finishes of the second physical room after read-back");

        ParameterReference ownership = settings.Walls.OwnershipParameter!;
        Element wall = finishes[0];
        FinishOwnershipMetadataService metadata = new();
        using (Transaction edit = new(document, "Manual reassignment"))
        {
            edit.Start();
            RevitFinishParameterAccess.FindParameter(wall, ownership)!.Set(otherIdentifier);
            edit.Commit();
        }
        FinishScheduleCalculationResult reassigned = service.BuildDetailed(document, manual);
        check(reassigned.Quantities.Occurrences.Single(item => item.ElementId == RevitElementIds.GetValue(wall.Id)).RoomId
            == RevitElementIds.GetValue(rooms[2].Id), "Manual text reassignment overrides saved physical ownership");
        check(metadata.ReadRoomId(wall, ownership, settings.RoomIdentifier.SourceKey, otherIdentifier) is null,
            "Changed ownership text invalidates the saved association");
        check(workflow.Apply(document, workflow.Prepare(document, manual)).Succeeded,
            "Manual reassignment writes the native schedule without geometry");
        check(workflow.Apply(document, workflow.Prepare(document, settings)).Succeeded,
            "Automatic assignment restores the original physical owner");

        using (Transaction edit = new(document, "Stale physical owner with unchanged text"))
        {
            edit.Start();
            metadata.Write(finishes[6], new FinishRoomAssignment(RevitElementIds.GetValue(finishes[6].Id), ownership,
                identifier, settings.RoomIdentifier.SourceKey, RevitElementIds.GetValue(rooms[0].Id)));
            edit.Commit();
        }
        FinishScheduleWritePreview refresh = workflow.Prepare(document, settings);
        check(refresh.OwnershipPlan.Changes.Count == 0, "Physical association can be refreshed without changing visible ownership");
        check(workflow.Apply(document, refresh).Succeeded
            && metadata.ReadRoomId(finishes[6], ownership, settings.RoomIdentifier.SourceKey, identifier) == RevitElementIds.GetValue(rooms[1].Id),
            "Automatic reassignment refreshes the physical owner even when text is unchanged");

        using (TransactionGroup rollback = new(document, "Metadata rollback"))
        {
            rollback.Start();
            using (Transaction edit = new(document, "Copy finish and change association"))
            {
                edit.Start();
                ElementId copyId = ElementTransformUtils.CopyElement(document, wall.Id, new XYZ(0, 0, 30)).Single();
                Element copy = document.GetElement(copyId);
                check(metadata.ReadRoomId(copy, ownership, settings.RoomIdentifier.SourceKey, identifier) is null,
                    "Copied finish does not inherit stale physical ownership");
                metadata.Write(wall, new FinishRoomAssignment(RevitElementIds.GetValue(wall.Id), ownership, identifier,
                    settings.RoomIdentifier.SourceKey, RevitElementIds.GetValue(rooms[1].Id)));
                edit.Commit();
            }
            rollback.RollBack();
        }
        check(metadata.ReadRoomId(wall, ownership, settings.RoomIdentifier.SourceKey, identifier) == RevitElementIds.GetValue(rooms[0].Id),
            "Transaction rollback restores physical ownership metadata");
    }

    private static void VerifyCalculation(FinishScheduleCalculationResult calculation, string identifier,
        IReadOnlyList<Room> rooms, IReadOnlyList<Element> finishes, Action<bool, string> check)
    {
        check(!calculation.Quantities.Warnings.Any(warning => warning.Code == FinishGeometryWarningCode.StoredOwnershipUnresolved),
            "Repeated identifiers do not exclude finish areas");
        check(calculation.Quantities.Occurrences.Count == finishes.Count
            && calculation.Quantities.Occurrences.Select(item => item.ElementId).Distinct().Count() == finishes.Count,
            "Every finish element is counted exactly once");
        double expectedArea = finishes.Sum(element => RevitAreaUnits.ToSquareMeters(
            element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble()));
        check(Math.Abs(calculation.Quantities.Occurrences.Sum(item => item.AreaSquareMeters) - expectedArea) < 1e-6,
            "Calculated area equals the sum of full native element areas");
        FinishAggregatedGroup shared = calculation.Aggregation!.Groups.Single(item => item.Output.RoomList == identifier);
        check(shared.RoomIds.OrderBy(id => id).SequenceEqual(rooms.Take(2).Select(room => RevitElementIds.GetValue(room.Id)).OrderBy(id => id)),
            "The shared identifier retains both physical room IDs");
        check(shared.Walls.Items.Select(item => item.Description.DisplayValue).OrderBy(value => value).SequenceEqual(new[] { "Paint", "Tile" }),
            "Different finishes of the same identifier are combined in one row");
        check(calculation.Aggregation.Groups.Count == 2, "Different identifiers retain their separate finish groups");
    }

    private static void ConfigureType(Element type, string classification, string description)
    {
        type.get_Parameter(BuiltInParameter.ALL_MODEL_MODEL).Set(classification);
        type.get_Parameter(BuiltInParameter.ALL_MODEL_DESCRIPTION).Set(description);
    }

    private static string ScheduleText(ViewSchedule schedule)
    {
        using TableData data = schedule.GetTableData();
        using TableSectionData body = data.GetSectionData(SectionType.Body);
        return string.Join("\n", Enumerable.Range(body.FirstRowNumber, body.NumberOfRows).SelectMany(row =>
            Enumerable.Range(body.FirstColumnNumber, body.NumberOfColumns).Select(column => schedule.GetCellText(SectionType.Body, row, column))));
    }
}
