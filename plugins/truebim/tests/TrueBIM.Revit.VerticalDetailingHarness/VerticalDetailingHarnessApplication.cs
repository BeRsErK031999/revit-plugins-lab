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
    private bool openingCopy;

    public Result OnStartup(UIControlledApplication application)
    {
        report = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_REPORT");
        if (!string.IsNullOrEmpty(report))
        {
            application.DialogBoxShowing += OnDialog;
            application.Idling += Run;
            application.ControlledApplication.FailuresProcessing += OnOpeningFailures;
            Progress("Harness loaded");
        }
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        application.Idling -= Run;
        application.DialogBoxShowing -= OnDialog;
        application.ControlledApplication.FailuresProcessing -= OnOpeningFailures;
        return Result.Succeeded;
    }

    private void OnOpeningFailures(object? sender, Autodesk.Revit.DB.Events.FailuresProcessingEventArgs args)
    {
        if (!openingCopy) return;
        var accessor = args.GetFailuresAccessor();
        var doc = accessor.GetDocument();
        bool resolved = false;
        foreach (var failure in accessor.GetFailureMessages())
        {
            var definition = failure.GetFailureDefinitionId();
            if (definition != BuiltInFailures.DimensionFailures.DimensionReferencesInvalid
                && definition != BuiltInFailures.DimensionFailures.SpotDimensionReferencesNoLongerHorizontal
                && definition != BuiltInFailures.DimensionFailures.SpotElevationMissesReference
                && definition != BuiltInFailures.DimensionFailures.SpotElevationLostEdgeReferences
                && definition != BuiltInFailures.DimensionFailures.SpotDimensionFormerSetMoribundCall) continue;
            var ids = failure.GetFailingElementIds();
            if (ids.Count == 0 || ids.Any(id => doc.GetElement(id) is not Dimension && doc.GetElement(id) is not MultiReferenceAnnotation)) continue;
            var resolution = failure.HasResolutionOfType(FailureResolutionType.DetachElements) ? FailureResolutionType.DetachElements
                : failure.HasResolutionOfType(FailureResolutionType.DeleteElements) ? FailureResolutionType.DeleteElements : FailureResolutionType.Invalid;
            if (resolution == FailureResolutionType.Invalid)
            {
                Progress("Opening dimension resolutions: " + string.Join(",", Enum.GetValues(typeof(FailureResolutionType)).Cast<FailureResolutionType>().Where(failure.HasResolutionOfType)));
                continue;
            }
            Progress("Opening copy: removing invalid dimension references, ids " + string.Join(",", ids));
            failure.SetCurrentResolutionType(resolution);
            accessor.ResolveFailure(failure); resolved = true;
        }
        if (resolved) args.SetProcessingResult(FailureProcessingResult.ProceedWithCommit);
    }

    private void OnDialog(object? sender, DialogBoxShowingEventArgs args)
    {
        if (args is TaskDialogShowingEventArgs dialog)
        {
            Progress("Dialog: " + dialog.DialogId + " " + dialog.Message);
            if (dialog.DialogId == "TaskDialog_License_Current_Status_Demo_Mode")
                dialog.OverrideResult((int)TaskDialogResult.Close);
            if (openingCopy && dialog.DialogId == "TaskDialog_Unresolved_References")
                dialog.OverrideResult(1002);
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
                    // The supplied fixture contains only the host and ALL link.
                    // Missing links stay unloaded in this disposable copy.
                    transmission.SetDesiredReferenceData(id, new FilePath(local), PathType.Absolute, File.Exists(local));
                    changed = true;
                }
                if (changed) { transmission.IsTransmitted = true; TransmissionData.WriteTransmissionData(copyPath, transmission); }
            }
            Progress("Opening " + model);
            BasicFileInfo info = BasicFileInfo.Extract(model);
            OpenOptions options = new();
            if (info.IsWorkshared) options.DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets;
            UIDocument ui;
            openingCopy = true;
            try { ui = app.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(model), options, false); }
            finally { openingCopy = false; }
            Document doc = ui.Document;
            Progress("Opened " + doc.Title);
            List<object> links = new();
            foreach (RevitLinkType type in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                string local = Path.Combine(Path.GetDirectoryName(model)!, type.Name);
                if (!local.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase)) local += ".rvt";
                try
                {
                    if (File.Exists(local) && !RevitLinkType.IsLoaded(doc, type.Id))
                    {
                        Progress("Reloading link " + local);
                        type.LoadFrom(ModelPathUtils.ConvertUserVisiblePathToModelPath(local), new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
                    }
                    links.Add(new { TypeId = type.Id.Value, type.Name, LocalExists = File.Exists(local), Loaded = RevitLinkType.IsLoaded(doc, type.Id) });
                }
                catch (Exception ex) { links.Add(new { type.Name, Error = ex.Message }); }
            }
            Progress("Collecting inventory");
            if (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_FAMILY_PROBE") == "1")
            {
                InspectBreakFamily(doc);
                if (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_FAMILY_ONLY") == "1")
                { Write(new { FatalError = (string?)null, Mode = "family-probe" }); return; }
            }
            if (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODE") == "acceptance")
            {
                string finalReport = report!;
                try { report = finalReport + ".apply.json"; TestDetailing(ui); }
                finally { report = finalReport; }
                TestInteractive(ui); return;
            }
            if (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODE") == "interactive") { TestInteractive(ui); return; }
            if (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODE") == "apply") { TestDetailing(ui); return; }
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().ToList();
            var placed = new HashSet<ElementId>(sheets.SelectMany(s => s.GetAllPlacedViews()));
            var nameFilters = (Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_VIEW_NAME_FILTER") ?? "")
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            var matchingSheets = sheets.Where(s => nameFilters.Any(f => s.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            var matchingPlaced = new HashSet<ElementId>(matchingSheets.SelectMany(s => s.GetAllPlacedViews()));
            foreach (ViewSheet sheet in matchingSheets) Export(doc, sheet, "reference");
            var views = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && (nameFilters.Length == 0 ? placed.Contains(v.Id) : matchingPlaced.Contains(v.Id) || nameFilters.Any(f => v.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0))
                    && v.ViewType != ViewType.DrawingSheet && v.ViewType != ViewType.Legend && v.ViewType != ViewType.Schedule).ToList();
            var details = new List<object>();
            foreach (View view in views)
            {
                Progress("View " + view.Id.Value + " " + view.Name);
                if (nameFilters.Length > 0) Export(doc, view, "reference");
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
            bool supplement6 = Environment.GetEnvironmentVariable("TRUEBIM_VERTICAL_MODE") == "supplement6";
            string outputReport = report!;
            if (supplement6) report = outputReport + ".inventory.json";
            Write(new {
                FatalError = (string?)null, Model = model, Links = links,
                Sheets = sheets.Select(s => new { Id = s.Id.Value, s.SheetNumber, s.Name, Params = Parameters(s), Views = s.GetAllPlacedViews().Select(id => id.Value).ToArray() }).ToList(),
                Viewports = new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>()
                    .Select(p => new { Id = p.Id.Value, ViewId = p.ViewId.Value, SheetId = p.SheetId.Value,
                        Number = p.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString(), Center = Point(p.GetBoxCenter()) }).ToList(),
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
            report = outputReport;
            if (supplement6)
            {
                var samples = views.Where(TrueBIM.App.Modules.RebarDetailing.RebarDetailingService.Supports)
                    .Where(v => v.Name.IndexOf("копия", StringComparison.OrdinalIgnoreCase) < 0).ToList();
                if (samples.Count == 0) throw new InvalidOperationException("No original section views on the requested sheet");
                Environment.SetEnvironmentVariable("TRUEBIM_VERTICAL_VIEWS", string.Join(",", samples.Select(v => v.Id.Value)));
                TestDetailing(ui); return;
            }
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
                TextPosition = SafePoint(() => dim.TextPosition),
                Curve = dim.Curve == null ? null : new { Start = SafePoint(() => dim.Curve.GetEndPoint(0)), End = SafePoint(() => dim.Curve.GetEndPoint(1)), Origin = dim.Curve is Line line ? Point(line.Origin) : null },
                References = dim.References.Cast<Reference>().Select(r => ReferenceInfo(doc, r)).ToList(),
                Segments = dim.Segments.Cast<DimensionSegment>().Select(s => new { s.Value, s.ValueOverride, s.Above, s.Below, s.Prefix, s.Suffix,
                    Origin = SafePoint(() => s.Origin), TextPosition = SafePoint(() => s.TextPosition) }).ToList()
            };
            else if (e is TextNote note) data = new { note.Text, Position = Point(note.Coord) };
            else if (e is CurveElement curve) data = new { Start = Point(curve.GeometryCurve.GetEndPoint(0)), End = Point(curve.GeometryCurve.GetEndPoint(1)), Style = curve.LineStyle.Name };
            else if (e is FamilyInstance family) data = new { Family = family.Symbol.FamilyName, Placement = family.Symbol.Family.FamilyPlacementType.ToString(),
                Location = family.Location is LocationPoint p ? Point(p.Point) : null,
                Start = family.Location is LocationCurve lc ? SafePoint(() => lc.Curve.GetEndPoint(0)) : null,
                End = family.Location is LocationCurve lc2 ? SafePoint(() => lc2.Curve.GetEndPoint(1)) : null,
                BasisX = Point(family.GetTransform().BasisX), BasisY = Point(family.GetTransform().BasisY), BasisZ = Point(family.GetTransform().BasisZ),
                TransformOrigin = Point(family.GetTransform().Origin), Params = Parameters(family),
                Leaders = family is AnnotationSymbol annotation ? annotation.GetLeaders().Cast<Leader>()
                    .Select(l => new { Anchor = SafePoint(() => l.Anchor), Elbow = SafePoint(() => l.Elbow), End = SafePoint(() => l.End) }).ToList() : null };
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
