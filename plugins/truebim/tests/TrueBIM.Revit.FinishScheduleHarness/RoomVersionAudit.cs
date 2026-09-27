using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.FinishScheduleHarness;

public static class RoomVersionAudit
{
    public static string Run(Document document, string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        List<string> passed = [];
        string? error = null;
        bool modifiedBefore = document.IsModified;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            passed.Add(message);
            File.AppendAllText(reportPath + ".progress.txt", message + Environment.NewLine);
        }
        FinishScheduleMetadataService metadata = new();
        FinishRoomScheduleBuilder builder = new(metadata, new AuditLogger());
        using TransactionGroup rollback = new(document, "TrueBIM: проверка связи строк с помещениями, с откатом");
        rollback.Start();
        try
        {
            ViewSchedule[] originals = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>().Where(metadata.IsManaged).ToArray();
            FinishRoomSchedulePlan plan = Plan("TrueBIM room linkage test", "TrueBIM_ROOM_LINK_TEST");
            FinishRoomSchedulePreflight preflight = builder.Preflight(document, plan);
            Check(preflight.Action == FinishRoomScheduleAction.Create,
                "Existing schedules can be migrated: " + string.Join("; ", preflight.Issues.Select(issue => issue.Message)));
            builder.PreservePreviousVersions(document, preflight);
            foreach (ViewSchedule original in originals)
            {
                Check(metadata.IsRoomBackedVersion(original), "Room-backed metadata: " + original.Name);
                Check(Members(original).Length > 0, "Selectable real rooms restored: " + original.Name);
                File.WriteAllText(reportPath + ".old-" + original.Id.IntegerValue + ".txt", Text(original));
            }
            Dictionary<ElementId, string> archivedText = originals.ToDictionary(view => view.Id, Text);
            Room a = NewRoom(document, "TrueBIM_TEST_A", "01 Paint\n02 Tile", "2,00\n10,00");
            Room b = NewRoom(document, "TrueBIM_TEST_B", "03 Plaster", "5,00");
            ViewSchedule first = Create(builder, document, plan);
            Check(Members(first).OrderBy(id => id).SequenceEqual(new[] { a.Id.IntegerValue, b.Id.IntegerValue }.OrderBy(id => id)),
                "New version contains exactly the two source rooms");
            Check(first.GetTableData().GetSectionData(SectionType.Header).NumberOfRows == 3,
                "Only the three header rows are static; quantities are in Body");
            Check(Text(first).Contains("02 Tile") && Text(first).Contains("10,00"), "Multiline values survive in native cells");
            string firstText = Text(first);
            ElementId placementId;
            ViewSheet sheet;
            using (Transaction transaction = new(document, "Temporary sheet placement"))
            {
                transaction.Start();
                sheet = ViewSheet.Create(document, ElementId.InvalidElementId);
                sheet.Name = "TrueBIM — связь строк с помещениями";
                placementId = ScheduleSheetInstance.Create(document, sheet.Id, first.Id, new XYZ(0.1, 0.7, 0)).Id;
                transaction.Commit();
            }
            using (Transaction transaction = new(document, "Change source parameters"))
            {
                transaction.Start();
                a.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).Set("Changed finish");
                a.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR).Set("99,00");
                transaction.Commit();
            }
            Room c = NewRoom(document, "TrueBIM_TEST_C", "New room", "3,00");
            ViewSchedule second = Create(builder, document, plan);
            Check(Text(first) == firstText, "Previous version keeps values after source parameters change");
            Check(Members(first).Length == 2 && Members(second).Length == 3, "New rooms do not leak into previous versions");
            Check(Text(second).Contains("99,00"), "New version uses updated values");
            Check(document.GetElement(placementId) is ScheduleSheetInstance instance && instance.ScheduleId == first.Id,
                "Sheet placement keeps its ID and schedule");

            // Reproduce the exact v2 defect, then exercise its automatic migration.
            ViewSchedule textArchive;
            using (Transaction transaction = new(document, "Legacy v2 fixture"))
            {
                transaction.Start();
                textArchive = (ViewSchedule)document.GetElement(first.Duplicate(ViewDuplicateOption.Duplicate));
                new FinishScheduleSnapshotService().Freeze(textArchive);
                Schema schema = Schema.Lookup(new Guid("C72569A6-4C99-4CD0-92D0-0198730E4551"));
                Entity entity = textArchive.GetEntity(schema);
                entity.Set(schema.GetField("SchemaVersion"), 2);
                textArchive.SetEntity(entity);
                transaction.Commit();
            }
            Check(Members(textArchive).Length == 0, "Legacy v2 fixture reproduces lost room selection");
            using (Transaction transaction = new(document, "Missing legacy room guard"))
            {
                transaction.Start();
                a.Number = "RENAMED_TEST_ROOM";
                document.Regenerate();
                Check(builder.Preflight(document, plan).Action == FinishRoomScheduleAction.Blocked,
                    "Missing first archive room blocks migration instead of losing its row");
                transaction.RollBack();
            }
            builder.PreservePreviousVersions(document, builder.Preflight(document, plan));
            Check(Members(textArchive).Length == 2 && Text(textArchive).Contains("10,00") && !Text(textArchive).Contains("99,00"),
                "Legacy v2 restores original values and both real room references");
            int parameterCount = new FilteredElementCollector(document).OfClass(typeof(SharedParameterElement)).GetElementCount();
            int scheduleCount = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).GetElementCount();
            using (TransactionGroup nested = new(document, "Atomic rollback test"))
            {
                nested.Start();
                Create(builder, document, plan);
                nested.RollBack();
            }
            Check(parameterCount == new FilteredElementCollector(document).OfClass(typeof(SharedParameterElement)).GetElementCount()
                && scheduleCount == new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).GetElementCount(),
                "Rollback removes the new version and all its parameter bindings");

            foreach (FinishScheduleHeaderMode mode in new[] { FinishScheduleHeaderMode.Standard, FinishScheduleHeaderMode.None })
            {
                FinishRoomSchedulePreflight alternate = builder.Preflight(document, plan.WithName("Link test " + mode));
                ViewSchedule view = (ViewSchedule)document.GetElement(new ElementId((int)builder.Apply(document, alternate, mode).ScheduleId));
                Check(Members(view).Length == 3 && Text(view).Contains("99,00"), "Native data in header mode " + mode);
            }

            // Test the complete production calculation and write workflow in the user's model.
            string profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TrueBIM", "BimTools", "finish-schedule", "settings.json");
            JavaScriptSerializer serializer = new() { MaxJsonLength = int.MaxValue };
            FinishScheduleSettings settings = ScheduleComparisonAudit.ReadSettings(
                serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(profilePath)));
            AuditLogger logger = new();
            FinishSchedulePreviewService calculation = new(new FinishElementCollector(logger),
                new FinishSchedulePreviewBuilder(new RoomScopeService(), new FinishClassificationService()), logger);
            FinishScheduleWriteWorkflow workflow = new(calculation, new RoomFinishWriteValueBuilder(), new FinishOwnershipValueBuilder(),
                new RoomFinishParameterWriter(new FinishParameterChangePlanner()), new FinishOwnershipWriter(new FinishParameterChangePlanner()),
                new FinishRoomSchedulePlanBuilder(), builder, logger);
            FinishScheduleWritePreview writePreview = workflow.Prepare(document, settings);
            Check(writePreview.CanApply, "Production preflight succeeds");
            FinishScheduleWriteResult writeResult = workflow.Apply(document, writePreview);
            Check(writeResult.Status == FinishScheduleWriteStatus.Applied && writeResult.Schedule is not null,
                "Production workflow creates a native version: " + writeResult.Message);
            ViewSchedule actualSchedule = (ViewSchedule)document.GetElement(new ElementId((int)writeResult.Schedule!.ScheduleId));
            Check(Members(actualSchedule).Length == writePreview.RoomCount && writePreview.RoomCount == 54,
                "All 54 model rooms are selectable from the new production schedule");
            Check(Text(actualSchedule).Contains("179,93") && Text(actualSchedule).Contains("401,00"),
                "Actual ceiling values are in native schedule cells");
            using (Transaction transaction = new(document, "Temporary production schedule sheet"))
            {
                transaction.Start();
                ViewSheet actualSheet = ViewSheet.Create(document, ElementId.InvalidElementId);
                actualSheet.Name = "TrueBIM — нативная ведомость модели";
                ScheduleSheetInstance.Create(document, actualSheet.Id, actualSchedule.Id, XYZ.Zero);
                transaction.Commit();
                Export(document, actualSheet, reportPath + ".model");
            }
            foreach (ViewSchedule original in originals)
                Check(Text(original) == archivedText[original.Id], "Original data unchanged by later releases: " + original.Name);
            Export(document, sheet, reportPath);
            Check(true, "Native version exported on a temporary sheet");
        }
        catch (Exception exception) { error = exception.ToString(); }
        finally { rollback.RollBack(); }
        string result = new JavaScriptSerializer().Serialize(new
        {
            FatalError = error, Passed = passed, RolledBack = true, ModifiedBefore = modifiedBefore,
            Document = document.Title, CompletedUtc = DateTime.UtcNow.ToString("o")
        });
        File.WriteAllText(reportPath, result);
        return result;
    }

    private static Room NewRoom(Document document, string number, string description, string area)
    {
        using Transaction transaction = new(document, "Temporary room fixture");
        transaction.Start();
        Room room = document.Create.NewRoom(document.Phases.Cast<Phase>().Last());
        room.Number = number;
        room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).Set(description);
        room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR).Set(area);
        room.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS).Set("TrueBIM_ROOM_LINK_TEST");
        transaction.Commit();
        return room;
    }

    private static FinishRoomSchedulePlan Plan(string name, string marker)
    {
        ParameterReference Ref(BuiltInParameter id, string label) => ParameterReference.BuiltIn(label, (long)id,
            ParameterBindingKind.Instance, ParameterStorageKind.String);
        return new FinishRoomSchedulePlan(name,
        [
            new(Ref(BuiltInParameter.ROOM_NUMBER, "Помещения"), "Помещения", 40, FinishRoomScheduleColumnKind.RoomList),
            new(Ref(BuiltInParameter.ROOM_FINISH_WALL, "Отделка"), "Отделка", 80, FinishRoomScheduleColumnKind.Description),
            new(Ref(BuiltInParameter.ROOM_FINISH_FLOOR, "Площадь"), "Площадь, м²", 25, FinishRoomScheduleColumnKind.Area),
            new(Ref(BuiltInParameter.ROOM_FINISH_CEILING, "Примечание"), "Примечание", 30, FinishRoomScheduleColumnKind.Note)
        ], new FinishRoomScheduleScopeFilter(ReportScopeKind.Section,
            Ref(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Комментарии"), ParameterStorageKind.String, marker), "link-test", []);
    }

    private static ViewSchedule Create(FinishRoomScheduleBuilder builder, Document document, FinishRoomSchedulePlan plan)
    {
        FinishRoomSchedulePreflight preflight = builder.Preflight(document, plan);
        builder.PreservePreviousVersions(document, preflight);
        return (ViewSchedule)document.GetElement(new ElementId((int)builder.Apply(document, preflight).ScheduleId));
    }

    private static int[] Members(ViewSchedule view) => new FilteredElementCollector(view.Document, view.Id)
        .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).ToArray();

    private static string Text(ViewSchedule view)
    {
        view.RefreshData();
        using TableData table = view.GetTableData();
        List<string> rows = [];
        foreach (SectionType type in new[] { SectionType.Header, SectionType.Body })
        {
            using TableSectionData section = table.GetSectionData(type);
            for (int row = section.FirstRowNumber; row <= section.LastRowNumber; row++)
                rows.Add(string.Join("|", Enumerable.Range(section.FirstColumnNumber, section.NumberOfColumns)
                    .Select(column => view.GetCellText(type, row, column))));
        }
        return string.Join("\n", rows);
    }

    private static void Export(Document document, ViewSheet sheet, string reportPath)
    {
        using ImageExportOptions export = new()
        {
            FilePath = reportPath, ExportRange = ExportRange.SetOfViews, HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG, ImageResolution = ImageResolution.DPI_150,
            ZoomType = ZoomFitType.FitToPage, PixelSize = 6000
        };
        export.SetViewsAndSheets(new List<ElementId> { sheet.Id });
        document.ExportImage(export);
    }

    private sealed class AuditLogger : ITrueBimLogger
    {
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
