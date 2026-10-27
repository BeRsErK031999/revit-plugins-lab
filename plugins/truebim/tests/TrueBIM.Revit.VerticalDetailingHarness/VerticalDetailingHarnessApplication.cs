using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace TrueBIM.Revit.VerticalDetailingHarness;

public sealed partial class VerticalDetailingHarnessApplication : IExternalApplication
{
    private string? report;
    private bool finished;

    public Result OnStartup(UIControlledApplication application)
    {
        report = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_REPORT");
        if (!string.IsNullOrEmpty(report))
        {
            application.DialogBoxShowing += OnDialog;
            application.Idling += Run;
            Progress("Harness loaded");
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= Run;
        application.DialogBoxShowing -= OnDialog;
        return Result.Succeeded;
    }

    private void OnDialog(object? sender, DialogBoxShowingEventArgs args)
    {
        if (args is TaskDialogShowingEventArgs dialog)
        {
            Progress("Dialog: " + dialog.DialogId + " " + dialog.Message);
            if (dialog.DialogId == "TaskDialog_License_Current_Status_Demo_Mode")
                dialog.OverrideResult((int)TaskDialogResult.Close);
        }
    }

    private void Progress(string text) => File.AppendAllText(report + ".progress.txt", DateTime.Now.ToString("s") + " " + text + "\n");

    private void Run(object? sender, IdlingEventArgs args)
    {
        if (finished || sender is not UIApplication app) return;
        finished = true;
        try
        {
            string model = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODEL")!;
            foreach (string copy in Directory.GetFiles(Path.GetDirectoryName(model)!, "*.rvt"))
            {
                ModelPath copyPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(copy);
                TransmissionData transmission = TransmissionData.ReadTransmissionData(copyPath);
                if (transmission == null) continue;
                bool changed = false;
                foreach (ElementId id in transmission.GetAllExternalFileReferenceIds())
                {
                    ExternalFileReference reference = transmission.GetLastSavedReferenceData(id);
                    if (reference.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;
                    string name = Path.GetFileName(ModelPathUtils.ConvertModelPathToUserVisiblePath(reference.GetPath()));
                    string local = Path.Combine(Path.GetDirectoryName(model)!, name);
                    if (!File.Exists(local)) continue;
                    transmission.SetDesiredReferenceData(id, new FilePath(local), PathType.Absolute, true);
                    changed = true;
                }
                if (changed) { transmission.IsTransmitted = true; TransmissionData.WriteTransmissionData(copyPath, transmission); }
            }
            Progress("Opening " + model);
            BasicFileInfo info = BasicFileInfo.Extract(model);
            OpenOptions options = new();
            if (info.IsWorkshared) options.DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets;
            UIDocument ui = app.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(model), options, false);
            Document doc = ui.Document;
            Progress("Opened " + doc.Title);
            List<object> links = new();
            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                string local = Path.Combine(Path.GetDirectoryName(model)!, type.Name);
                if (!local.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) local += ".rvt";
                try
                {
                    if (File.Exists(local))
                    {
                        Progress("Reloading link " + local);
                        type.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(local), new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                    }
                    links.Add(new { TypeId = type.Id.Value, type.Name, LocalExists = File.Exists(local), Loaded = RevitLinkType.IsLoaded(doc, type.Id) });
                }
                catch (Exception ex) { links.Add(new { type.Name, Error = ex.Message }); }
            }
            Progress("Collecting inventory");
            if (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODE") == "apply") { TestDetailing(ui); return; }
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            var placed = new HashSet<ElementId>(sheets.SelectMany(s => s.GetAllPlacedViews()));
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && placed.Contains(v.Id) && v.ViewType != ViewType.Legend && v.ViewType != ViewType.Schedule).ToList();
            var details = new List<object>();
            foreach (View view in views)
            {
                Progress("View " + view.Id.Value + " " + view.Name);
                var elements = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType().ToElements();
                details.Add(new {
                    Id = view.Id.Value, view.Name, ViewType = view.ViewType.ToString(), view.Scale,
                    Direction = Point(view.ViewDirection), Right = Point(view.RightDirection), Up = Point(view.UpDirection),
                    Origin = Point(view.Origin), CropMin = Point(view.CropBox.Min), CropMax = Point(view.CropBox.Max),
                    view.CropBoxActive, TemplateId = view.ViewTemplateId.Value,
                    VisibleCategories = elements.GroupBy(e => e.Category?.Name ?? "none").ToDictionary(g => g.Key, g => g.Count()),
                    Annotations = elements.Where(e => e.OwnerViewId == view.Id).Select(e => Annotation(doc, e)).ToList()
                });
            }
            var rebarDocuments = new List<Document> { doc };
            rebarDocuments.AddRange(new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Select(l => l.GetLinkDocument()).Where(d => d != null));
            Write(new {
                FatalError = (string?)null, Model = model, Links = links,
                Sheets = sheets.Select(s => new { Id = s.Id.Value, s.SheetNumber, s.Name, Params = Parameters(s), Views = s.GetAllPlacedViews().Select(id => id.Value).ToArray() }).ToList(),
                Views = details,
                AnnotationTypes = new FilteredElementCollector(doc).WhereElementIsElementType().Where(e => e.Category?.CategoryType == CategoryType.Annotation)
                    .Select(e => new { Id = e.Id.Value, e.Name, Class = e.GetType().Name, Family = (e as FamilySymbol)?.FamilyName, Category = e.Category?.Name, Params = Parameters(e) }).ToList(),
                ModelDocuments = rebarDocuments.Distinct().Select(d => new { d.Title,
                    Counts = new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e => e is Rebar || e is RebarInSystem || e is AreaReinforcement || e is Wall || e is Floor || e.Category?.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                        .GroupBy(e => e.GetType().Name).ToDictionary(g => g.Key, g => g.Count()),
                    BarSamples = new FilteredElementCollector(d).WhereElementIsNotElementType().Where(e => e is Rebar || e is RebarInSystem).Take(8)
                        .Select(e => new { Id = e.Id.Value, e.Name, Class = e.GetType().Name, Params = Parameters(e) }).ToList()
                }).ToList()
            });
            Progress("Done");
        }
        catch (Exception ex) { Progress(ex.ToString()); Write(new { FatalError = ex.ToString() }); }
    }

    private static object Annotation(Document doc, Element e)
    {
        object? data = null;
        try
        {
            if (e is IndependentTag tag) data = new {
                Text = tag.TagText, Position = Point(tag.TagHeadPosition), tag.HasLeader, Orientation = tag.TagOrientation.ToString(),
                References = tag.GetTaggedReferences().Select(r => ReferenceInfo(doc, r)).ToList(),
                Leaders = tag.GetTaggedReferences().Where(r => tag.HasLeader).Select(r => new { Elbow = SafePoint(() => tag.GetLeaderElbow(r)), End = SafePoint(() => tag.GetLeaderEnd(r)) }).ToList()
            };
            else if (e is Dimension dim) data = new {
                Value = dim.Value, dim.ValueOverride, dim.Above, dim.Below, dim.Prefix, dim.Suffix,
                Curve = dim.Curve == null ? null : new { Start = SafePoint(() => dim.Curve.GetEndPoint(0)), End = SafePoint(() => dim.Curve.GetEndPoint(1)), Origin = dim.Curve is Line line ? Point(line.Origin) : null },
                References = dim.References.Cast<Reference>().Select(r => ReferenceInfo(doc, r)).ToList(),
                Segments = dim.Segments.Cast<DimensionSegment>().Select(s => new { s.Value, s.ValueOverride, s.Above, s.Below, s.Prefix, s.Suffix, Origin = SafePoint(() => s.Origin) }).ToList()
            };
            else if (e is TextNote note) data = new { note.Text, Position = Point(note.Coord) };
            else if (e is CurveElement curve) data = new { Start = Point(curve.GeometryCurve.GetEndPoint(0)), End = Point(curve.GeometryCurve.GetEndPoint(1)), Style = curve.LineStyle.Name };
            else if (e is FamilyInstance family) data = new { Family = family.Symbol.FamilyName, Location = family.Location is LocationPoint p ? Point(p.Point) : null, Params = Parameters(family) };
        }
        catch (Exception ex) { data = new { Error = ex.Message }; }
        return new { Id = e.Id.Value, e.Name, Class = e.GetType().Name, Category = e.Category?.Name, TypeId = e.GetTypeId().Value, GroupId = e.GroupId.Value, Data = data };
    }

    private static object ReferenceInfo(Document doc, Reference reference)
    {
        Element? e = doc.GetElement(reference.ElementId);
        Document target = doc;
        if (e is RevitLinkInstance link && reference.LinkedElementId != ElementId.InvalidElementId)
        { target = link.GetLinkDocument(); e = target?.GetElement(reference.LinkedElementId); }
        string stable;
        try { stable = reference.ConvertToStableRepresentation(doc); } catch { stable = ""; }
        return new { ElementId = reference.ElementId.Value, LinkedId = reference.LinkedElementId.Value,
            Class = e?.GetType().Name, Category = e?.Category?.Name, e?.Name, Stable = stable, RefType = reference.ElementReferenceType.ToString(),
            HostId = e is Rebar rebar ? rebar.GetHostId().Value : e is RebarInSystem system ? system.GetHostId().Value : -1,
            Params = e is Rebar || e is RebarInSystem ? Parameters(e) : null };
    }

    private static Dictionary<string, string> Parameters(Element e)
    {
        Dictionary<string, string> values = new();
        foreach (Parameter p in e.Parameters)
        {
            try { values[p.Definition.Name] = p.AsValueString() ?? p.AsString() ?? ""; } catch { }
        }
        return values;
    }

    private static double[]? SafePoint(Func<XYZ> value) { try { return Point(value()); } catch { return null; } }
    private static double[]? Point(XYZ? point) => point == null ? null : new[] { point.X, point.Y, point.Z };
    private void Write(object value) => File.WriteAllText(report!, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value));
}
