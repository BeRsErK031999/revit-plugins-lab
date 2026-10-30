using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.SelectionBox;

namespace TrueBIM.Revit.SelectionBoxHarness;

/// <summary>Opt-in native tests against disposable copies; never saves the opened documents.</summary>
public sealed class SelectionBoxHarnessApplication : IExternalApplication
{
    public static SelectionBoxHarnessApplication Current { get; private set; } = null!;
    private string? output;
    private bool ran;
    private int passed;
    private bool opened;
    private bool resume;
    private int phase;
    private List<Reference> refs = [];
    private List<RevitLinkInstance> links = [];
    private IReadOnlyList<XYZ> points = [];
    private ElementId standardId = ElementId.InvalidElementId;
    public Result OnStartup(UIControlledApplication app)
    {
        Current = this;
        output = Environment.GetEnvironmentVariable("TRUEBIM_SELECTION_BOX_QA");
        if (!string.IsNullOrEmpty(output))
        {
            app.Idling += Run;
            app.DialogBoxShowing += OnDialog;
            app.CreateRibbonTab("TrueBIM QA");
            app.CreateRibbonPanel("TrueBIM QA", "SelectionBox").AddItem(new PushButtonData(
                "Run", "Run SelectionBox QA", typeof(SelectionBoxHarnessApplication).Assembly.Location,
                typeof(SelectionBoxHarnessCommand).FullName));
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
        // Missing links in the disposable copies are intentionally replaced with the supplied files.
        if (args.DialogId == "TaskDialog_Unresolved_References") args.OverrideResult(1002);
    }
    private void Log(string message) => File.AppendAllText(Path.Combine(output!, "report.txt"), message + Environment.NewLine);
    private void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("FAIL " + label);
        passed++;
        Log("PASS " + label);
    }
    private static Element ModelElement(Document doc) => new FilteredElementCollector(doc).OfClass(typeof(Wall))
        .First(e => e.get_BoundingBox(null) != null);

    private static View3D Execute(UIApplication app)
    {
        SelectionBoxRequest.Start(app);
        return (View3D)app.ActiveUIDocument.ActiveView;
    }

    private void Run(object? sender, IdlingEventArgs args)
    {
        if (ran && (!resume || SelectionBoxRequest.IsPending)) return;
        ran = true;
        resume = false;
        try
        {
            UIApplication app = (UIApplication)sender!;
            string[] models = Directory.GetFiles(Path.Combine(output!, "models"), "*.rvt");
            string hostPath = models.Single(p => Path.GetFileName(p).Contains("КР_ALL"));
            if (!opened)
            {
            foreach (string path in models)
            {
                ModelPath modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
                TransmissionData? data = TransmissionData.ReadTransmissionData(modelPath);
                if (data == null) continue;
                foreach (ElementId id in data.GetAllExternalFileReferenceIds())
                {
                    ExternalFileReference reference = data.GetLastSavedReferenceData(id);
                    if (reference.ExternalFileReferenceType == ExternalFileReferenceType.RevitLink)
                        data.SetDesiredReferenceData(id, reference.GetPath(), reference.PathType, false);
                }
                data.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(modelPath, data);
            }
            Log("Opening disposable structural model");
            using OpenOptions options = new() { DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets };
            app.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(hostPath), options, false);
            opened = true;
            ran = false;
            return;
            }
            RevitCommandId command = RevitCommandId.LookupCommandId("CustomCtrl_%CustomCtrl_%TrueBIM QA%SelectionBox%Run");
            if (command != null && app.CanPostCommand(command)) app.PostCommand(command);
            else Log("READY: click TrueBIM QA > Run SelectionBox QA");
        }
        catch (Exception ex) { Log(ex.ToString()); File.WriteAllText(Path.Combine(output!, "failed"), ex.ToString()); }
    }

    public void RunChecks(UIApplication app)
    {
        try
        {
            string[] models = Directory.GetFiles(Path.Combine(output!, "models"), "*.rvt");
            string hostPath = models.Single(p => Path.GetFileName(p).Contains("КР_ALL"));
            UIDocument ui = app.ActiveUIDocument;
            Document doc = ui.Document;
            if (phase == 0)
            {
            Check(!doc.IsWorkshared, "detached test host is not workshared");
            Element hostElement = ModelElement(doc);
            foreach (string path in models.Where(p => p != hostPath))
            {
                Log("Linking " + Path.GetFileName(path));
                using Transaction tx = new(doc, "QA load copied RVT");
                tx.Start();
                LinkLoadResult result = RevitLinkType.Create(doc, ModelPathUtils.ConvertUserVisiblePathToModelPath(path), new RevitLinkOptions(false));
                RevitLinkInstance link = RevitLinkInstance.Create(doc, result.ElementId);
                ElementTransformUtils.MoveElement(doc, link.Id, new XYZ(links.Count * 150, links.Count * 40, 5));
                ElementTransformUtils.RotateElement(doc, link.Id, Line.CreateBound(XYZ.Zero, XYZ.BasisZ), 0.37 * (links.Count + 1));
                tx.Commit();
                links.Add(link);
                Check(link.GetLinkDocument() != null, "loaded " + Path.GetFileName(path));
            }
            refs = [new Reference(hostElement)];
            foreach (RevitLinkInstance link in links)
                refs.Add(new Reference(ModelElement(link.GetLinkDocument())).CreateLinkReference(link));
            ui.Selection.SetReferences(refs);
            Check(SelectionBoxService.ReadSelection(ui).Count == 4, "preselection retains host and three linked elements");
            points = SelectionBoxBounds.Resolve(doc, SelectionBoxService.ReadSelection(ui));
            Check(points.Count == 32, "all eight corners from all four elements");
            foreach (RevitLinkInstance link in links)
            {
                BoundingBoxXYZ bb = ModelElement(link.GetLinkDocument()).get_BoundingBox(null);
                XYZ expected = link.GetTotalTransform().OfPoint(bb.Min);
                Check(points.Any(p => p.DistanceTo(expected) < 1e-7), "transformed linked bounding box " + link.Id);
            }
            ui.ActiveView = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate);
            Close3D(ui);
            using (Transaction tx = new(doc, "QA remove default view"))
            {
                tx.Start();
                foreach (View3D v in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v => !v.IsTemplate && v.Name == "{3D}").ToList())
                    doc.Delete(v.Id);
                tx.Commit();
            }
            ui.Selection.SetReferences(refs);
            phase = 1;
            resume = true;
            SelectionBoxRequest.Start(app);
            return;
            }
            ViewPlan plan = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().First(v => !v.IsTemplate);
            if (phase == 1)
            {
            View3D target = (View3D)ui.ActiveView;
            standardId = target.Id;
            Check(target.Name == "{3D}" && ui.ActiveView.Id == target.Id, "create and activate missing default 3D");
            Check(target.IsSectionBoxActive && !target.IsTemporaryHideIsolateActive(), "section box active, no isolation");
            AssertBounds(target, points);
            ElementId template = target.ViewTemplateId;
            ViewDetailLevel detail = target.DetailLevel;
            DisplayStyle style = target.DisplayStyle;
            ViewOrientation3D camera = target.GetOrientation();
            ui.Selection.SetReferences(refs);
            Check(Execute(app).Id == target.Id, "reuse active 3D");
            Check(target.ViewTemplateId == template && target.DetailLevel == detail && target.DisplayStyle == style,
                "preserve template/detail/display style");
            Check(target.GetOrientation().ForwardDirection.IsAlmostEqualTo(camera.ForwardDirection), "preserve camera orientation");
            ui.ActiveView = plan;
            ui.Selection.SetReferences(refs);
            Check(Execute(app).Id == target.Id, "reuse open 3D from plan");
            foreach (View sourceView in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .Where(v => !v.IsTemplate && (v.ViewType == ViewType.DrawingSheet || v.ViewType == ViewType.Section
                    || v.ViewType == ViewType.Elevation || v.ViewType == ViewType.Schedule))
                .GroupBy(v => v.ViewType).Select(g => g.First()).ToList())
            {
                ui.ActiveView = sourceView;
                ui.Selection.SetReferences(refs);
                Check(Execute(app).Id == target.Id, "launch from " + sourceView.ViewType);
            }
            ui.ActiveView = plan;
            Close3D(ui);
            ui.Selection.SetReferences(refs);
            Check(Execute(app).Id == target.Id, "reopen closed existing default 3D");
            using (Transaction tx = new(doc, "QA rotate section box"))
            {
                tx.Start();
                BoundingBoxXYZ box = target.GetSectionBox();
                box.Transform = Transform.CreateRotationAtPoint(XYZ.BasisZ, 0.41, new XYZ(12, 4, 3));
                target.SetSectionBox(box);
                tx.Commit();
            }
            Transform rotation = target.GetSectionBox().Transform;
            ui.Selection.SetReferences(refs);
            Execute(app);
            Check(target.GetSectionBox().Transform.BasisX.IsAlmostEqualTo(rotation.BasisX), "preserve rotated section box");
            AssertBounds(target, points);
            using (Transaction tx = new(doc, "QA mirror link"))
            {
                tx.Start();
                ElementTransformUtils.MirrorElements(doc, new[] { links[0].Id }, Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero), false);
                tx.Commit();
            }
            ui.Selection.SetReferences(new[] { refs[1] });
            points = SelectionBoxBounds.Resolve(doc, SelectionBoxService.ReadSelection(ui));
            Check(links[0].GetTotalTransform().HasReflection, "mirrored link fixture");
            Execute(app);
            AssertBounds(target, points);
            BoundingBoxXYZ before = target.GetSectionBox();
            ui.Selection.SetReferences(new List<Reference>());
            bool rejected = false;
            try { Execute(app); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && target.GetSectionBox().Min.IsAlmostEqualTo(before.Min), "empty selection changes nothing");
            ui.Selection.SetReferences(new[] { new Reference(plan) });
            rejected = false;
            try { Execute(app); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && target.GetSectionBox().Min.IsAlmostEqualTo(before.Min), "non-model selection changes nothing");
            ui.ActiveView = plan;
            Close3D(ui);
            Log("Enabling worksharing on disposable host");
            doc.EnableWorksharing("QA shared levels", "QA model");
            ui.Selection.SetReferences(refs);
            phase = 2;
            resume = true;
            SelectionBoxRequest.Start(app);
            return;
            }
            View3D personal = (View3D)ui.ActiveView;
            Check(personal.Name == "{3D - " + app.Application.Username + "}" && personal.Id != standardId, "create personal 3D in workshared model");
            AssertBounds(personal, SelectionBoxBounds.Resolve(doc, refs));
            ui.ActiveView = plan;
            Close3D(ui);
            ui.Selection.SetReferences(refs);
            Check(Execute(app).Id == personal.Id, "reopen personal view without duplicates");
            Log("SUCCESS " + passed + " checks; no model was saved.");
            File.WriteAllText(Path.Combine(output!, "done"), passed.ToString());
        }
        catch (Exception ex) { Log(ex.ToString()); File.WriteAllText(Path.Combine(output!, "failed"), ex.ToString()); }
    }
    private static void Close3D(UIDocument ui)
    {
        foreach (UIView v in ui.GetOpenUIViews())
            if (ui.Document.GetElement(v.ViewId) is View3D) v.Close();
    }
    private void AssertBounds(View3D view, IReadOnlyList<XYZ> points)
    {
        BoundingBoxXYZ box = view.GetSectionBox();
        XYZ[] local = points.Select(box.Transform.Inverse.OfPoint).ToArray();
        double p = SelectionBoxBounds.PaddingFeet;
        Check(Math.Abs(box.Min.X - local.Min(v => v.X) + p) < 1e-6
            && Math.Abs(box.Min.Y - local.Min(v => v.Y) + p) < 1e-6
            && Math.Abs(box.Min.Z - local.Min(v => v.Z) + p) < 1e-6
            && Math.Abs(box.Max.X - local.Max(v => v.X) - p) < 1e-6
            && Math.Abs(box.Max.Y - local.Max(v => v.Y) - p) < 1e-6
            && Math.Abs(box.Max.Z - local.Max(v => v.Z) - p) < 1e-6,
            "exact enclosure with 300 mm on all six faces");
    }
}

[Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
public sealed class SelectionBoxHarnessCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        SelectionBoxHarnessApplication.Current.RunChecks(commandData.Application);
        return Result.Succeeded;
    }
}
