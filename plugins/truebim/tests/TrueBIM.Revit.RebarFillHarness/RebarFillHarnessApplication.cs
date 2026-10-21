using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace TrueBIM.Revit.RebarFillHarness;

public sealed class RebarFillHarnessApplication : IExternalApplication
{
    private string? report;
    private bool openingCopy;
    public Result OnStartup(UIControlledApplication app)
    {
        report = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_FILL_REPORT");
        if (!string.IsNullOrEmpty(report))
        {
            app.Idling += Run;
            app.DialogBoxShowing += OnDialog;
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) { app.Idling -= Run; app.DialogBoxShowing -= OnDialog; return Result.Succeeded; }

    private void OnDialog(object? sender, DialogBoxShowingEventArgs args)
    {
        if (args is not TaskDialogShowingEventArgs dialog) return;
        if (dialog.DialogId == "TaskDialog_License_Current_Status_Demo_Mode")
            dialog.OverrideResult((int)TaskDialogResult.Close);
        if (openingCopy && dialog.DialogId == "TaskDialog_Missing_Third_Party_Updater")
        {
            File.AppendAllText(report + ".progress.txt", "\nCopy-only updater notice: " + dialog.Message);
            dialog.OverrideResult(1001);
        }
    }

    private void OnOpeningFailures(object? sender, FailuresProcessingEventArgs args)
    {
        if (!openingCopy) return;
        FailuresAccessor failures = args.GetFailuresAccessor();
        foreach (FailureMessageAccessor failure in failures.GetFailureMessages())
        {
            File.AppendAllText(report + ".progress.txt", "\nOpening failure: " + failure.GetSeverity() + ": " + failure.GetDescriptionText());
            if (failure.GetSeverity() == FailureSeverity.Warning) failures.DeleteWarning(failure);
        }
    }

    private void Run(object? sender, IdlingEventArgs args)
    {
        UIApplication app = (UIApplication)sender!;
        app.Idling -= Run;
        Document? doc = null;
        try
        {
            string path = Environment.GetEnvironmentVariable("TRUEBIM_REBAR_FILL_MODEL");
            File.WriteAllText(report + ".progress.txt", "Opening copy: " + path);
            using BasicFileInfo info = BasicFileInfo.Extract(path);
            OpenOptions options = new();
            if (info.IsWorkshared) options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            openingCopy = true;
            app.Application.FailuresProcessing += OnOpeningFailures;
            try { doc = app.Application.OpenDocumentFile(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), options); }
            finally { openingCopy = false; app.Application.FailuresProcessing -= OnOpeningFailures; }
            File.AppendAllText(report + ".progress.txt", "\nOpened: " + doc.Title);
            List<Element> all = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().ToList();
            List<FamilyInstance> nested = all.OfType<FamilyInstance>().Where(x => x.SuperComponent != null).ToList();
            if (Environment.GetEnvironmentVariable("TRUEBIM_REBAR_FILL_REGRESSION") == "1")
            {
                File.AppendAllText(report + ".progress.txt", "\nRunning regression tests.");
                Write(new { Format = info.Format, Scenarios = RebarFillRegression.Run(doc) });
                return;
            }
            var groups = nested.GroupBy(x => new { x.Symbol.FamilyName, Category = x.Category?.Name })
                .Select(g => new { g.Key, Count = g.Count(), SampleId = g.First().Id.IntegerValue,
                    ParentId = g.First().SuperComponent.Id.IntegerValue, Shared = g.First().Symbol.Family.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() }).ToList();
            var samples = nested.GroupBy(x => x.Symbol.Family.Id.IntegerValue).Select(g => (Element)g.First())
                .Concat(all.OfType<RebarInSystem>().Take(2)).Concat(all.OfType<Rebar>().Take(2)).ToList();
            var probes = new List<object>();
            File.WriteAllText(report + ".inventory.json", new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(new { Format = info.Format, NestedGroups = groups, RebarCount = all.OfType<Rebar>().Count(), RebarInSystemCount = all.OfType<RebarInSystem>().Count() }));
            foreach (Element e in samples)
            {
                File.AppendAllText(report + ".progress.txt", "\nInspect " + e.Id);
                var parameters = new List<object>();
                foreach (Parameter p in e.Parameters)
                {
                    string? error = null; bool? set = null; string? after = null;
                    if (p.StorageType == StorageType.String)
                    {
                        using Transaction t = new(doc, "Probe parameter — rollback");
                        t.Start();
                        try { set = p.Set("TrueBIM_probe_" + Guid.NewGuid().ToString("N")); doc.Regenerate(); after = p.AsString(); }
                        catch (Exception ex) { error = ex.Message; }
                        finally { t.RollBack(); }
                    }
                    parameters.Add(new { Name = p.Definition.Name, Id = p.Id.IntegerValue, Storage = p.StorageType.ToString(),
                        ReadOnly = p.IsReadOnly, p.UserModifiable, p.HasValue, Value = p.AsValueString() ?? (p.StorageType == StorageType.String ? p.AsString() : null),
                        Guid = p.IsShared ? p.GUID.ToString() : null, Set = set, After = after, Error = error });
                }
                probes.Add(new { Id = e.Id.IntegerValue, e.Name, Class = e.GetType().Name, Category = e.Category?.Name,
                    Parent = (e as FamilyInstance)?.SuperComponent?.Id.IntegerValue,
                    Parameters = parameters });
                File.WriteAllText(report + ".probes.json", new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(probes));
            }
            Write(new { Format = info.Format, NestedGroups = groups, RebarCount = all.OfType<Rebar>().Count(),
                RebarInSystemCount = all.OfType<RebarInSystem>().Count(), Probes = probes,
                Schedules = all.OfType<ViewSchedule>().Where(v => !v.IsTemplate).Select(v => new { Id = v.Id.IntegerValue, v.Name }).ToList() });
        }
        catch (Exception ex) { Write(new { FatalError = ex.ToString() }); }
        finally { doc?.Close(false); }
    }

    private void Write(object data) => File.WriteAllText(report!, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(data));
}
