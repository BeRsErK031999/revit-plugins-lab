using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.FinishScheduleHarness;

public sealed class FinishScheduleHarnessApplication : IExternalApplication
{
    private readonly List<string> passed = [];
    private readonly FinishScheduleMetadataService metadata = new();
    private string? reportPath;
    private UIDocument? uiDocument;
    private bool busy;
    private bool done;

    public Result OnStartup(UIControlledApplication application)
    {
        reportPath = Environment.GetEnvironmentVariable("TRUEBIM_FINISH_HARNESS_REPORT");
        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            Progress("Loaded; waiting for Idling.");
            application.Idling += OnIdling;
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= OnIdling;
        return Result.Succeeded;
    }

    private void OnIdling(object? sender, IdlingEventArgs args)
    {
        if (busy || done || sender is not UIApplication application) return;
        busy = true;
        try
        {
            string? modelPath = Environment.GetEnvironmentVariable("TRUEBIM_FINISH_HARNESS_MODEL");
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                Progress("Opening model copy: " + modelPath);
                using Document model = application.Application.OpenDocumentFile(modelPath);
                new FinishModelAudit(Progress).Run(model, reportPath!);
                model.Close(false);
                Check(true, "Model audit completed on disposable copy");
                WriteReport(null);
                return;
            }
            if (uiDocument is null)
            {
                Progress("Opening disposable template.");
                uiDocument = application.OpenAndActivateDocument(@"C:\ProgramData\Autodesk\RVT 2022\Templates\English\DefaultMetric.rte");
                return;
            }
            Run();
            WriteReport(null);
        }
        catch (Exception exception)
        {
            WriteReport(exception.ToString());
        }
        finally
        {
            busy = false;
            if (!done) args.SetRaiseWithoutDelay();
        }
    }

    private void Run()
    {
        Document document = uiDocument!.Document;
        Room room;
        using (Transaction transaction = new(document, "Finish test fixture"))
        {
            transaction.Start();
            room = document.Create.NewRoom(document.Phases.Cast<Phase>().Last());
            room.Number = "101";
            room.get_Parameter(BuiltInParameter.ROOM_NAME).Set("Test room");
            room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).Set("01 Coat\r\n02 Tile");
            room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR).Set("2,00\r\n10,00");
            transaction.Commit();
        }
        FinishRoomScheduleBuilder builder = new(metadata, new TestLogger(Progress));
        FinishRoomSchedulePlan plan = Plan("Finish regression");
        Progress("Creating first version.");
        ViewSchedule first = Create(builder, document, plan);
        Check(metadata.IsSnapshot(first), "New version marked as snapshot");
        string original = Text(first);
        File.WriteAllText(reportPath + ".first.txt", original);
        Check(original.Contains("01 Coat") && original.Contains("10,00"), "First version contains displayed room data");
        CheckTitleBorders(first);
        ViewSheet sheet;
        ScheduleSheetInstance placement;
        using (Transaction transaction = new(document, "Place first version"))
        {
            transaction.Start();
            sheet = ViewSheet.Create(document, ElementId.InvalidElementId);
            sheet.Name = "Finish regression";
            placement = ScheduleSheetInstance.Create(document, sheet.Id, first.Id, new XYZ(0.1, 0.8, 0));
            transaction.Commit();
        }
        ElementId placementId = placement.Id;
        XYZ placementPoint = placement.Point;
        ChangeRoom(document, room, "03 Paint", "139,79");
        Progress("Creating second version after changing room values.");
        ViewSchedule second = Create(builder, document, plan);
        Check(second.Id != first.Id && second.Name == "Finish regression (2)", "Second version gets a new ID and numbered name");
        Check(Text(first) == original, "Old snapshot unchanged after room parameters changed");
        Check(Text(second).Contains("139,79") && !Text(second).Contains("01 Coat"), "New snapshot uses new room data");
        Check(document.GetElement(placementId) is ScheduleSheetInstance oldPlacement
              && oldPlacement.ScheduleId == first.Id && oldPlacement.Point.IsAlmostEqualTo(placementPoint),
            "Existing sheet placement keeps ID, schedule and position");

        Progress("Creating old dynamic schedule and testing rollback.");
        ViewSchedule legacy = CreateLegacy(document, plan.WithName("Legacy finish"));
        string oldDynamic = Text(legacy);
        ElementId legacyPlacementId;
        using (Transaction transaction = new(document, "Place legacy version"))
        {
            transaction.Start();
            legacyPlacementId = ScheduleSheetInstance.Create(document, sheet.Id, legacy.Id, new XYZ(0.1, 0.4, 0)).Id;
            transaction.Commit();
        }
        FinishRoomSchedulePreflight preflight = builder.Preflight(document, plan);
        Check(preflight.LegacyScheduleIds.SequenceEqual(new[] { (long)legacy.Id.IntegerValue }), "Preflight finds legacy live version");
        int countBefore = ScheduleCount(document);
        using (TransactionGroup group = new(document, "Rollback test"))
        {
            group.Start();
            builder.PreservePreviousVersions(document, preflight);
            ChangeRoom(document, room, "Rollback value", "99,00");
            builder.Apply(document, preflight);
            group.RollBack();
        }
        Check(!metadata.IsSnapshot(legacy) && Text(legacy) == oldDynamic
              && room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR).AsString() == "139,79"
              && ScheduleCount(document) == countBefore, "Outer rollback restores legacy data, metadata, parameters and schedule count");
        using (TransactionGroup group = new(document, "Archive before writing new room values"))
        {
            group.Start();
            builder.PreservePreviousVersions(document, preflight);
            string frozenLegacy = Text(legacy);
            Check(frozenLegacy.Contains("139,79"), "Legacy snapshot captures pre-write values");
            ChangeRoom(document, room, "04 Wallpaper", "25,00");
            builder.Apply(document, preflight);
            Check(Text(legacy) == frozenLegacy, "Legacy snapshot remains unchanged during new calculation");
            group.Assimilate();
        }
        Check(metadata.IsSnapshot(legacy) && document.GetElement(legacyPlacementId) is ScheduleSheetInstance item
              && item.ScheduleId == legacy.Id, "Legacy conversion preserves sheet instance and marks snapshot");
        Progress("Testing alternate header modes.");
        ViewSchedule standard = Create(builder, document, Plan("Standard header"), FinishScheduleHeaderMode.Standard);
        Check(Text(standard).Contains("25,00"), "Standard-header fallback contains data");
        CheckTitleBorders(standard);
        ViewSchedule noHeader = Create(builder, document, Plan("No header"), FinishScheduleHeaderMode.None);
        Check(Text(noHeader).Contains("25,00") && !Text(noHeader).Contains("No header"), "No-header fallback contains data without adding a title");
        Progress("Exporting sheet for visual verification.");
        ImageExportOptions export = new()
        {
            FilePath = reportPath + ".sheet",
            ExportRange = ExportRange.SetOfViews,
            HLRandWFViewsFileType = ImageFileType.PNG,
            ShadowViewsFileType = ImageFileType.PNG,
            ImageResolution = ImageResolution.DPI_150,
            ZoomType = ZoomFitType.FitToPage,
            PixelSize = 2400
        };
        export.SetViewsAndSheets(new List<ElementId> { sheet.Id });
        document.ExportImage(export);
        Check(true, "Fixture sheet exported for visual verification");
    }

    private static ViewSchedule Create(FinishRoomScheduleBuilder builder, Document document,
        FinishRoomSchedulePlan plan, FinishScheduleHeaderMode mode = FinishScheduleHeaderMode.Custom)
    {
        FinishRoomSchedulePreflight preflight = builder.Preflight(document, plan);
        using TransactionGroup group = new(document, "Create finish test version");
        group.Start();
        builder.PreservePreviousVersions(document, preflight);
        FinishRoomScheduleApplyResult result = builder.Apply(document, preflight, mode);
        group.Assimilate();
        return (ViewSchedule)document.GetElement(new ElementId((int)result.ScheduleId));
    }

    private ViewSchedule CreateLegacy(Document document, FinishRoomSchedulePlan plan)
    {
        using Transaction transaction = new(document, "Create legacy live version");
        transaction.Start();
        ViewSchedule schedule = ViewSchedule.CreateSchedule(document, new ElementId(BuiltInCategory.OST_Rooms));
        schedule.Name = plan.ScheduleName;
        foreach (FinishRoomScheduleColumn column in plan.Columns)
        {
            ScheduleField field = schedule.Definition.AddField(ScheduleFieldType.Instance, new ElementId((int)column.Parameter.BuiltInParameterId!.Value));
            field.ColumnHeading = column.Heading;
            field.SheetColumnWidth = column.WidthMillimeters / 304.8;
        }
        metadata.Write(schedule, plan);
        Schema schema = Schema.Lookup(new Guid("C72569A6-4C99-4CD0-92D0-0198730E4551"));
        Entity entity = schedule.GetEntity(schema);
        entity.Set(schema.GetField("SchemaVersion"), 1);
        schedule.SetEntity(entity);
        transaction.Commit();
        return schedule;
    }

    private static FinishRoomSchedulePlan Plan(string name)
    {
        FinishRoomScheduleColumn[] columns =
        [
            Column(BuiltInParameter.ROOM_NUMBER, "Помещения", 40, FinishRoomScheduleColumnKind.RoomList),
            Column(BuiltInParameter.ROOM_FINISH_WALL, "Стены", 80, FinishRoomScheduleColumnKind.Description),
            Column(BuiltInParameter.ROOM_FINISH_FLOOR, "Площадь, м²", 25, FinishRoomScheduleColumnKind.Area),
            Column(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, "Примечание", 30, FinishRoomScheduleColumnKind.Note)
        ];
        return new FinishRoomSchedulePlan(name, columns, FinishRoomScheduleScopeFilter.EntireProject(), "harness-v20", columns.Select(column => column.Parameter.StableKey));
    }

    private static FinishRoomScheduleColumn Column(BuiltInParameter parameter, string heading, double width, FinishRoomScheduleColumnKind kind) =>
        new(ParameterReference.BuiltIn(heading, (long)parameter, ParameterBindingKind.Instance, ParameterStorageKind.String), heading, width, kind);

    private static void ChangeRoom(Document document, Room room, string description, string area)
    {
        using Transaction transaction = new(document, "Change fixture room data");
        transaction.Start();
        room.get_Parameter(BuiltInParameter.ROOM_FINISH_WALL).Set(description);
        room.get_Parameter(BuiltInParameter.ROOM_FINISH_FLOOR).Set(area);
        transaction.Commit();
    }

    private void CheckTitleBorders(ViewSchedule schedule)
    {
        using TableData table = schedule.GetTableData();
        using TableSectionData header = table.GetSectionData(SectionType.Header);
        using TableCellStyle style = header.GetTableCellStyle(header.FirstRowNumber, header.FirstColumnNumber);
        Check(new[] { style.BorderTopLineStyle, style.BorderBottomLineStyle, style.BorderLeftLineStyle, style.BorderRightLineStyle }
            .All(id => id == ElementId.InvalidElementId), $"Title has no borders: {schedule.Name}");
    }

    private static string Text(ViewSchedule schedule)
    {
        schedule.RefreshData();
        using TableData table = schedule.GetTableData();
        List<string> rows = [];
        foreach (SectionType type in new[] { SectionType.Header, SectionType.Body })
        {
            using TableSectionData section = table.GetSectionData(type);
            for (int row = section.FirstRowNumber; row <= section.LastRowNumber; row++)
                rows.Add(string.Join("|", Enumerable.Range(section.FirstColumnNumber, section.NumberOfColumns).Select(column => schedule.GetCellText(type, row, column))));
        }
        return string.Join("\n", rows);
    }

    private static int ScheduleCount(Document document) => new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).GetElementCount();
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        passed.Add(name);
        Progress("PASS " + name);
    }
    private void Progress(string message) => File.AppendAllText(reportPath + ".progress.txt", message + Environment.NewLine);
    private void WriteReport(string? error)
    {
        done = true;
        File.WriteAllText(reportPath!, new JavaScriptSerializer().Serialize(new { FatalError = error, Passed = passed }));
    }
    private sealed class TestLogger(Action<string> write) : ITrueBimLogger
    {
        public void Info(string message) => write(message);
        public void Warning(string message) => write(message);
        public void Error(string message, Exception? exception = null) => write(message + " " + exception);
    }
}
