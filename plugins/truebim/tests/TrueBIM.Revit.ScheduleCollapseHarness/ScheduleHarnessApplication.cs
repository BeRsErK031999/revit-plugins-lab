using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.ScheduleColumnCollapse.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.ScheduleCollapseHarness;

public sealed class ScheduleHarnessApplication : IExternalApplication
{
    private string? root;
    private bool ran;
    public Result OnStartup(UIControlledApplication app)
    {
        root = Environment.GetEnvironmentVariable("TRUEBIM_VRS_QA");
        if (!string.IsNullOrEmpty(root))
        {
            app.Idling += Run;
            app.DialogBoxShowing += OnDialog;
        }
        return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app)
    {
        app.Idling -= Run;
        app.DialogBoxShowing -= OnDialog;
        return Result.Succeeded;
    }
    private void OnDialog(object? sender, DialogBoxShowingEventArgs args)
    {
        File.AppendAllText(Path.Combine(root!, "dialogs.txt"), args.DialogId + Environment.NewLine);
        if (args.DialogId == "TaskDialog_Unresolved_References") args.OverrideResult(1002);
    }
    private void Run(object? sender, IdlingEventArgs args)
    {
        if (ran) return;
        ran = true;
        try
        {
            UIApplication app = (UIApplication)sender!;
            string path = Directory.GetFiles(Path.Combine(root!, "models"), "*.rvt").Single();
            ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
            TransmissionData? transmission = TransmissionData.ReadTransmissionData(modelPath);
            if (transmission != null)
            {
                foreach (ElementId id in transmission.GetAllExternalFileReferenceIds())
                {
                    ExternalFileReference reference = transmission.GetLastSavedReferenceData(id);
                    if (reference.ExternalFileReferenceType == ExternalFileReferenceType.RevitLink)
                        transmission.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
                }
                transmission.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(modelPath, transmission);
            }
            using OpenOptions options = new() { DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets };
            Document doc = app.OpenAndActivateDocument(modelPath, options, false).Document;
            List<ViewSchedule> schedules = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>().Where(s => !s.IsTemplate && s.Name == "Арматура • Ведомость расхода стали ППП").ToList();
            if (schedules.Count != 1) throw new InvalidOperationException("Expected exactly one PPP schedule.");
            List<object> audits = new();
            foreach (ViewSchedule schedule in schedules)
            {
                using Transaction tx = new(doc, "Inspect VRS copy");
                tx.Start();
                var fields = schedule.Definition.GetFieldOrder().Select(schedule.Definition.GetField).ToList();
                var before = fields.Select(f => new { Id = f.FieldId.IntegerValue, Name = f.GetName(), f.ColumnHeading, f.IsHidden, Spec = f.GetSpecTypeId().TypeId }).ToArray();
                foreach (ScheduleField field in fields) field.IsHidden = false;
                doc.Regenerate();
                using TableData table = schedule.GetTableData();
                using TableSectionData body = table.GetSectionData(SectionType.Body);
                body.RefreshData();
                List<object> rows = new();
                for (int row = body.FirstRowNumber; row <= body.LastRowNumber; row++)
                {
                    List<object> cells = new();
                    for (int col = body.FirstColumnNumber; col <= body.LastColumnNumber; col++)
                    {
                        using TableMergedCell merged = body.GetMergedCell(row, col);
                        cells.Add(new { Column = col, Text = schedule.GetCellText(SectionType.Body, row, col),
                            Type = body.GetCellType(row, col).ToString(), ParamId = body.GetCellParamId(row, col).Value,
                            Merge = new[] { merged.Top, merged.Bottom, merged.Left, merged.Right } });
                    }
                    rows.Add(new { Row = row, Cells = cells });
                }
                audits.Add(new { schedule.Name, Id = schedule.Id.Value, Fields = before, Rows = rows });
                tx.RollBack();
            }
            Write("audit.json", audits);
            foreach (ViewSchedule schedule in schedules) Verify(doc, schedule);
            File.WriteAllText(Path.Combine(root!, "done"), "Acceptance complete");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root!, "failed"), ex.ToString()); }
    }
    private void Write(string name, object value) => File.WriteAllText(Path.Combine(root!, name),
        new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value));

    private void Verify(Document doc, ViewSchedule schedule)
    {
        var ids = schedule.Definition.GetFieldOrder().ToList();
        var initial = ids.ToDictionary(id => id, id => schedule.Definition.GetField(id).IsHidden);
        var headings = ids.ToDictionary(id => id, id => schedule.Definition.GetField(id).ColumnHeading);
        bool showTitle = schedule.Definition.ShowTitle, showHeaders = schedule.Definition.ShowHeaders;
        List<object> runs = new();
        using TransactionGroup group = new(doc, "VRS regression; rollback all changes");
        group.Start();
        ScheduleColumnCollapseService service = new(new ScheduleColumnVisibilityAnalyzer(), new TestLogger(root!));
        Dictionary<ScheduleFieldId, bool>? reference = null;
        foreach (string mode in new[] { "original", "shown", "hidden", "mixed" })
        {
            using (Transaction setup = new(doc, mode))
            {
                setup.Start();
                foreach (ScheduleFieldId id in ids)
                {
                    ScheduleField field = schedule.Definition.GetField(id);
                    field.IsHidden = mode == "original" ? initial[id] : mode == "hidden" || mode == "mixed" && id.IntegerValue % 2 == 0;
                }
                setup.Commit();
            }
            var before = ids.ToDictionary(id => id, id => schedule.Definition.GetField(id).IsHidden);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var result = service.Collapse(schedule);
                if (!result.Succeeded) throw new InvalidOperationException(result.Message);
                // Independent oracle from the supplied PPP model: six data/total rows,
                // 105 zero-only consumption fields and these 12 non-zero fields.
                int[] nonzeroIds = { 45, 57, 60, 61, 62, 63, 64, 65, 67, 69, 72, 89 };
                if (result.HiddenColumnCount != 105 || result.VisibleColumnCount != 12 || result.UnchangedColumnCount != 21)
                    throw new InvalidOperationException("Unexpected PPP field counts.");
                var after = ids.ToDictionary(id => id, id => schedule.Definition.GetField(id).IsHidden);
                foreach (ScheduleFieldId id in ids)
                {
                    ScheduleField field = schedule.Definition.GetField(id);
                    if (!ScheduleColumnVisibilityAnalyzer.IsConsumptionField(field.GetName()) && before[id] != after[id])
                        throw new InvalidOperationException("Unrelated field changed: " + field.GetName());
                    if (ScheduleColumnVisibilityAnalyzer.IsConsumptionField(field.GetName()) && after[id] == nonzeroIds.Contains(id.IntegerValue))
                        throw new InvalidOperationException("Wrong consumption visibility: " + field.GetName());
                    if (field.ColumnHeading != headings[id]) throw new InvalidOperationException("Heading changed: " + field.GetName());
                    if (reference != null && ScheduleColumnVisibilityAnalyzer.IsConsumptionField(field.GetName()) && after[id] != reference[id])
                        throw new InvalidOperationException("Visibility depends on initial state: " + field.GetName());
                }
                if (schedule.Definition.ShowTitle != showTitle || schedule.Definition.ShowHeaders != showHeaders)
                    throw new InvalidOperationException("Schedule header options changed.");
                reference ??= after;
                runs.Add(new { Mode = mode, Attempt = attempt + 1, result.HiddenColumnCount, result.VisibleColumnCount, result.UnchangedColumnCount,
                    Fields = ids.Select(id => new { Id = id.IntegerValue, Name = schedule.Definition.GetField(id).GetName(), Hidden = after[id] }).ToArray() });
                Write("acceptance.json", runs);
            }
        }
        group.RollBack();
    }

    private sealed class TestLogger(string folder) : ITrueBimLogger
    {
        public void Info(string message) => File.AppendAllText(Path.Combine(folder, "service.log"), message + Environment.NewLine);
        public void Warning(string message) => Info(message);
        public void Error(string message, Exception? exception = null) => Info(message + exception);
    }
}
