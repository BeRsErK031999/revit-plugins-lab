using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.FinishScheduleHarness;

internal sealed class FinishModelAudit(Action<string> progress) : ITrueBimLogger
{
    private readonly List<string> passed = [];

    public void Run(Document document, string reportPath)
    {
        WriteGeometryAudit(document, reportPath);
        FinishScheduleSettings defaults = FinishScheduleSettings.CreateDefault() with
        {
            DescriptionParameter = ParameterReference.BuiltIn("Имя типа", (long)BuiltInParameter.SYMBOL_NAME_PARAM,
                ParameterBindingKind.Type, ParameterStorageKind.String),
            WriteOwnership = true,
            ScheduleName = "TrueBIM — проверка полной площади"
        };
        FinishScheduleSettings settings = new FinishSchedulePreferredParameterResolver(
            new FinishScheduleParameterOptionService(new ParameterCatalogMatcher())).Resolve(defaults,
            new ParameterCatalogService(this).Collect(document), ParameterCatalogService.TargetCategories);
        FinishSchedulePreviewService service = new(new FinishElementCollector(this),
            new FinishSchedulePreviewBuilder(new RoomScopeService(), new FinishClassificationService()), this);
        FinishScheduleCalculationResult calculation = service.BuildDetailed(document, settings);
        JavaScriptSerializer serializer = new() { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        object report = new
        {
            Settings = settings,
            Calculation = new
            {
                Rooms = calculation.Collection.Rooms,
                ClassifiedElements = calculation.Build.Classification.Elements,
                Types = calculation.Collection.Types.Values.ToArray(),
                calculation.Quantities,
                calculation.RoomSnapshots,
                Groups = calculation.Aggregation?.Groups,
                calculation.Preview.Performance
            },
            Elements = calculation.Collection.Walls.Concat(calculation.Collection.Floors).Concat(calculation.Collection.Ceilings)
                .Select(candidate =>
                {
                    Element element = document.GetElement(new ElementId((int)candidate.ElementId));
                    Element type = document.GetElement(element.GetTypeId());
                    return new
                    {
                        candidate.ElementId, candidate.TypeId, Category = candidate.PhysicalCategory.ToString(),
                        TypeName = type.Name, LevelId = element.LevelId.IntegerValue,
                        Area = (element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * 0.09290304,
                        candidate.Bounds,
                        Parameters = element.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.String)
                            .Select(p => new { Name = p.Definition.Name, Value = p.AsString() }).ToArray(),
                        TypeParameters = type.Parameters.Cast<Parameter>().Where(p => p.StorageType == StorageType.String)
                            .Select(p => new { Name = p.Definition.Name, Value = p.AsString() }).ToArray()
                    };
                }).ToArray()
        };
        File.WriteAllText(reportPath + ".audit.json", serializer.Serialize(report));
        progress("Audit written: " + reportPath + ".audit.json");
        VerifyQuantities(document, calculation);
        VerifyRoomHeight(document, service, settings, calculation, reportPath, serializer);
        VerifySchedule(document, service, settings, calculation, reportPath);
        File.WriteAllText(reportPath + ".checks.json", serializer.Serialize(passed));
    }

    private void VerifyQuantities(Document document, FinishScheduleCalculationResult calculation)
    {
        Check(calculation.Quantities.Occurrences.Count > 0, "Model has calculated finish elements");
        Check(calculation.Quantities.Occurrences.GroupBy(item => item.ElementId).All(group => group.Count() == 1),
            "Each finish element has exactly one owner and is counted once");
        Check(calculation.Quantities.Occurrences.All(item => Math.Abs(item.AreaSquareMeters
            - document.GetElement(new ElementId((int)item.ElementId)).get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble() * 0.09290304) < 1e-6),
            "Every occurrence equals the full native Revit area");
        Check(Math.Round(calculation.Quantities.Occurrences.Where(item => item.RoomId == 2026734
            && item.Category == FinishPreviewCategory.Walls).Sum(item => item.AreaSquareMeters), 2) == 172.01,
            "Room 101 walls equal the user's control schedule: 172.01 square meters");
        Check(calculation.Quantities.Occurrences.Single(item => item.ElementId == 2341307).RoomId == 1936556,
            "Wall 2341307 (34.96 square meters) belongs only to room 106");
        Check(calculation.Quantities.Occurrences.Single(item => item.ElementId == 2366558).RoomId == 1936520,
            "Wall 2366558 (42.56 square meters) belongs only to room 129");
        Check(calculation.Quantities.Occurrences.Single(item => item.ElementId == 1956941).RoomId == 2257169,
            "Complex floor 1956941 belongs to room 201 and uses its full area");
        Check(new[] { 2409164, 2409963, 2410218, 2410686, 2410728 }.All(id =>
                calculation.Quantities.Occurrences.Single(item => item.ElementId == id).RoomId == 2257169),
            "Five previously missed window reveals belong to room 201");
        Check(new[] { 2416714, 2417028 }.All(id => calculation.Quantities.Occurrences.Single(item => item.ElementId == id).RoomId == 2002325),
            "Window reveals of room 204 remain on their own storey");
        foreach (RoomFinishSnapshot room in calculation.RoomSnapshots!.Rooms)
        {
            Check(Math.Abs(room.Ceilings.Items.Sum(item => item.AreaSquareMeters)
                - calculation.Quantities.Occurrences.Where(item => item.RoomId == room.RoomId && item.Category == FinishPreviewCategory.Ceilings)
                    .Sum(item => item.AreaSquareMeters)) < 1e-6, "Ceiling quantity reaches room snapshot " + room.Identifier);
        }
        FinishRoomCandidateSnapshot room211 = calculation.Build.RoomScope.SelectedRooms.Single(room => room.Number == "211");
        Check(calculation.Quantities.Occurrences.Any(item => item.RoomId == room211.ElementId && item.Category == FinishPreviewCategory.Ceilings),
            "Room 211 has a ceiling occurrence");
        FinishAggregatedGroup group211 = calculation.Aggregation!.Groups.Single(group => group.RoomIds.Contains(room211.ElementId));
        Check(group211.Ceilings.Items.Count > 0 && group211.Output.Ceilings!.AreaText != "—", "Room 211 ceiling reaches schedule output");
    }

    private void VerifyRoomHeight(Document document, FinishSchedulePreviewService service, FinishScheduleSettings settings,
        FinishScheduleCalculationResult baseline, string reportPath, JavaScriptSerializer serializer)
    {
        long roomId = baseline.Build.RoomScope.SelectedRooms.Single(room => room.Number == "101").ElementId;
        Room room = (Room)document.GetElement(new ElementId((int)roomId));
        List<object> variants = [];
        FinishOccurrence[]? first = null;
        foreach (double height in new[] { 2438.4, 6000.0 })
        {
            using TransactionGroup group = new(document, "Temporary room height regression");
            group.Start();
            using (Transaction transaction = new(document, "Change room 101 height"))
            {
                transaction.Start();
                room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL).Set(room.LevelId);
                room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET).Set(height / 304.8);
                transaction.Commit();
            }
            FinishScheduleCalculationResult variant = service.BuildDetailed(document, settings);
            FinishOccurrence[] current = variant.Quantities.Occurrences.Where(item => item.RoomId == roomId)
                .OrderBy(item => item.ElementId).ToArray();
            variants.Add(new { HeightMillimeters = height, Occurrences = current });
            if (first is not null)
                Check(first.Select(item => (item.ElementId, item.AreaSquareMeters)).SequenceEqual(
                    current.Select(item => (item.ElementId, item.AreaSquareMeters))), "Room 101 full finish set and areas are invariant at 2438.4 and 6000 mm");
            first = current;
            group.RollBack();
        }
        File.WriteAllText(reportPath + ".heights.json", serializer.Serialize(variants));
    }

    private void VerifySchedule(Document document, FinishSchedulePreviewService service, FinishScheduleSettings settings,
        FinishScheduleCalculationResult calculation, string reportPath)
    {
        FinishScheduleMetadataService metadata = new();
        Dictionary<ElementId, string> previous = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>().Where(metadata.IsManaged).ToDictionary(schedule => schedule.Id, ScheduleText);
        FinishRoomScheduleBuilder builder = new(new FinishScheduleMetadataService(), this);
        FinishScheduleWriteWorkflow workflow = new(service, new RoomFinishWriteValueBuilder(), new FinishOwnershipValueBuilder(),
            new RoomFinishParameterWriter(new FinishParameterChangePlanner()), new FinishOwnershipWriter(new FinishParameterChangePlanner()),
            new FinishRoomSchedulePlanBuilder(), builder, this);
        FinishScheduleWritePreview preview = workflow.Prepare(document, settings);
        FinishScheduleWriteResult result = workflow.Apply(document, preview);
        File.WriteAllText(reportPath + ".write.json", new JavaScriptSerializer().Serialize(result));
        Check(result.Schedule is not null, "Model schedule created");
        ViewSchedule first = (ViewSchedule)document.GetElement(new ElementId((int)result.Schedule!.ScheduleId));
        string firstText = ScheduleText(first);
        ViewSheet sheet;
        ScheduleSheetInstance placement;
        using (Transaction transaction = new(document, "Temporary schedule visual verification"))
        {
            transaction.Start();
            sheet = ViewSheet.Create(document, ElementId.InvalidElementId);
            placement = ScheduleSheetInstance.Create(document, sheet.Id, first.Id, XYZ.Zero);
            transaction.Commit();
        }
        using (TableData data = first.GetTableData())
        using (TableSectionData header = data.GetSectionData(SectionType.Header))
            File.WriteAllText(reportPath + ".layout.json", new JavaScriptSerializer().Serialize(
                Enumerable.Range(header.FirstRowNumber, header.NumberOfRows).Select(row => new
                {
                    Row = row, HeightMillimeters = header.GetRowHeight(row) * 304.8,
                    Cells = Enumerable.Range(header.FirstColumnNumber, header.NumberOfColumns).Select(column => new
                    {
                        Column = column, Text = first.GetCellText(SectionType.Header, row, column),
                        Font = header.GetTableCellStyle(row, column).FontName,
                        Size = header.GetTableCellStyle(row, column).TextSize
                    }).ToArray()
                }).ToArray()));
        foreach (KeyValuePair<ElementId, string> old in previous)
        {
            string after = ScheduleText((ViewSchedule)document.GetElement(old.Key));
            File.WriteAllText(reportPath + ".legacy-" + old.Key.IntegerValue + ".before.txt", old.Value);
            File.WriteAllText(reportPath + ".legacy-" + old.Key.IntegerValue + ".after.txt", after);
            Check(ContentLines(after).SequenceEqual(ContentLines(old.Value)),
                "Existing model schedule retains displayed values: " + old.Key.IntegerValue);
        }
        using (TableData table = first.GetTableData())
        using (TableSectionData header = table.GetSectionData(SectionType.Header))
        using (TableCellStyle style = header.GetTableCellStyle(0, 0))
            Check(new[] { style.BorderTopLineStyle, style.BorderBottomLineStyle, style.BorderLeftLineStyle, style.BorderRightLineStyle }
                .All(id => id == ElementId.InvalidElementId), "Native schedule title has no borders");
        File.WriteAllText(reportPath + ".schedule.txt", firstText);
        foreach (FinishAggregatedGroup group in calculation.Aggregation!.Groups.Where(group => group.Ceilings.Items.Count > 0))
            Check(HasCeilingRow(first, group),
                "Ceiling group appears in the native schedule: " + group.Output.RoomList);
        FinishScheduleWritePreview secondPreview = workflow.Prepare(document, settings);
        File.WriteAllText(reportPath + ".repeat-changes.json", new JavaScriptSerializer().Serialize(new
        {
            Rooms = secondPreview.RoomPlan.Changes, Ownership = secondPreview.OwnershipPlan.Changes
        }));
        Check(secondPreview.TotalChangeCount == 0, "Repeated calculation does not rewrite unchanged parameters");
        FinishScheduleWriteResult second = workflow.Apply(document, secondPreview);
        Check(second.Schedule is not null && second.Schedule.ScheduleId != result.Schedule.ScheduleId,
            "Repeated run creates a separate schedule version");
        Check(ScheduleText(first) == firstText, "Previous model schedule keeps frozen values");
        Check(document.GetElement(placement.Id) is ScheduleSheetInstance preserved
              && preserved.ScheduleId == first.Id && preserved.OwnerViewId == sheet.Id
              && preserved.Point.IsAlmostEqualTo(XYZ.Zero), "Previous version retains its placement on the sheet");
        ImageExportOptions export = new()
        {
            FilePath = reportPath + ".sheet", ExportRange = ExportRange.SetOfViews,
            HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
            ImageResolution = ImageResolution.DPI_150, ZoomType = ZoomFitType.FitToPage, PixelSize = 9000
        };
        export.SetViewsAndSheets(new List<ElementId> { sheet.Id });
        document.ExportImage(export);
        Check(true, "Native schedule sheet exported for visual verification");
    }

    private static bool HasCeilingRow(ViewSchedule schedule, FinishAggregatedGroup group)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData section = table.GetSectionData(SectionType.Header);
        for (int row = section.FirstRowNumber; row <= section.LastRowNumber; row++)
            if (schedule.GetCellText(SectionType.Header, row, 0) == group.Output.RoomList
                && Normalize(schedule.GetCellText(SectionType.Header, row, 4)) == Normalize(group.Output.Ceilings!.AreaText))
                return true;
        return false;
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\u00a0', ' ').Trim();

    private static IEnumerable<string> ContentLines(string value) => Normalize(value).Split('\n')
        .Where(line => !string.IsNullOrWhiteSpace(line));

    private static void WriteGeometryAudit(Document document, string reportPath)
    {
        using FinishRoomGeometryCache cache = new(document);
        object[] rooms = new[] { 2257169, 2026734, 2002199 }.Select(id =>
        {
            cache.TryGet(id, out FinishRoomGeometryData? geometry, out _);
            using SpatialElementBoundaryOptions options = new() { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
            return (object)new
            {
                Id = id,
                CreatedPhase = geometry!.Room.CreatedPhaseId.IntegerValue,
                RoomPhase = geometry.Room.get_Parameter(BuiltInParameter.ROOM_PHASE_ID)?.AsElementId().IntegerValue,
                Faces = geometry!.Solid.Faces.OfType<PlanarFace>().Select(face => new
                {
                    Area = face.Area * 0.09290304,
                    Normal = Point(face.FaceNormal), Origin = Point(face.Origin),
                    Vertices = face.GetEdgesAsCurveLoops().Select(loop => loop.Select(curve => Point(curve.GetEndPoint(0))).ToArray()).ToArray()
                }).ToArray(),
                Boundaries = geometry.Room.GetBoundarySegments(options).Select(loop => loop.Select(segment => new
                {
                    Element = segment.ElementId.IntegerValue,
                    Start = Point(segment.GetCurve().GetEndPoint(0)), End = Point(segment.GetCurve().GetEndPoint(1))
                }).ToArray()).ToArray()
            };
        }).ToArray();
        FinishElementGeometryCache elements = new(document);
        object[] solids = new[] { 1956941, 2409164, 2416714, 2822885, 2466464 }.Select(id => (object)new
        {
            Id = id,
            Faces = elements.Get(id).Geometry!.Solids.SelectMany(solid => solid.Faces.OfType<PlanarFace>()).Select(face => new
            {
                Area = face.Area * 0.09290304, Normal = Point(face.FaceNormal), Origin = Point(face.Origin),
                Vertices = face.GetEdgesAsCurveLoops().Select(loop => loop.Select(curve => Point(curve.GetEndPoint(0))).ToArray()).ToArray()
            }).ToArray()
        }).ToArray();
        object[] openings = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Windows)
            .OfType<FamilyInstance>().Select(window =>
            {
                BoundingBoxXYZ box = window.get_BoundingBox(null);
                return (object)new
                {
                    Id = window.Id.IntegerValue, From = window.FromRoom?.Number, To = window.ToRoom?.Number,
                    Min = Point(box.Min), Max = Point(box.Max), Host = window.Host?.Id.IntegerValue,
                    HostType = window.Host?.GetType().Name, Width = (window.Host as Wall)?.Width,
                    Orientation = window.Host is Wall host ? Point(host.Orientation) : Point(window.FacingOrientation),
                    CreatedPhase = window.CreatedPhaseId.IntegerValue
                };
            }).ToArray();
        using SpatialElementBoundaryOptions boundaryOptions = new() { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
        object[] footprints = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms).OfType<Room>()
            .Where(room => room.Area > 1e-8).Select(room => (object)new
            {
                Id = room.Id.IntegerValue, room.Number, Level = room.LevelId.IntegerValue,
                Loops = room.GetBoundarySegments(boundaryOptions).Select(loop => loop
                    .SelectMany(segment => segment.GetCurve().Tessellate().Select(Point)).ToArray()).ToArray()
            }).ToArray();
        File.WriteAllText(reportPath + ".geometry.json", new JavaScriptSerializer { MaxJsonLength = int.MaxValue }
            .Serialize(new { Rooms = rooms, Elements = solids, Openings = openings, Footprints = footprints }));
    }

    private static double[] Point(XYZ point) => [point.X, point.Y, point.Z];

    private static string ScheduleText(ViewSchedule schedule)
    {
        schedule.RefreshData();
        using TableData table = schedule.GetTableData();
        List<string> values = [];
        foreach (SectionType type in new[] { SectionType.Header, SectionType.Body })
        {
            using TableSectionData section = table.GetSectionData(type);
            for (int row = section.FirstRowNumber; row <= section.LastRowNumber; row++)
                for (int column = section.FirstColumnNumber; column <= section.LastColumnNumber; column++)
                {
                    TableMergedCell merged = section.GetMergedCell(row, column);
                    values.Add(merged.Top == row && merged.Left == column ? schedule.GetCellText(type, row, column) : string.Empty);
                }
        }
        return string.Join("\n", values);
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        passed.Add(message);
        progress("PASS " + message);
    }

    public void Info(string message) => progress(message);
    public void Warning(string message) => progress(message);
    public void Error(string message, Exception? exception = null) => progress(message + " " + exception);
}
