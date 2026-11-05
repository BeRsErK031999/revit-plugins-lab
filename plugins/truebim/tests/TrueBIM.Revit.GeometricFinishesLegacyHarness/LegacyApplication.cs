using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using TrueBIM.App.Modules.GeometricFinishes;

namespace TrueBIM.Revit.GeometricFinishesLegacyHarness;

public sealed class LegacyApplication : IExternalApplication
{
    private bool ran;
    public Result OnStartup(UIControlledApplication app) { app.Idling += Run; return Result.Succeeded; }
    public Result OnShutdown(UIControlledApplication app) { app.Idling -= Run; return Result.Succeeded; }
    private void Run(object? sender, IdlingEventArgs args)
    {
        string root = Environment.GetEnvironmentVariable("TRUEBIM_FINISHES_LEGACY_QA");
        if (ran || string.IsNullOrEmpty(root)) return;
        ran = true;
        try
        {
            UIApplication app = (UIApplication)sender!;
            Document doc = app.Application.NewProjectDocument(UnitSystem.Metric);
            Level level;
            Room room;
            using (Transaction tx = new(doc, "QA curved room"))
            {
                tx.Start();
                level = Level.Create(doc, 0);
                WallType wallType = new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic);
                for (int i = 0; i < 2; i++)
                    Wall.Create(doc, Arc.Create(XYZ.Zero, 10, i * Math.PI, (i + 1) * Math.PI, XYZ.BasisX, XYZ.BasisY), wallType.Id, level.Id, 12, 0, false, false);
                ViewFamilyType vt = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.FloorPlan);
                ViewPlan view = ViewPlan.Create(doc, vt.Id, level.Id);
                CurveArray holes = new();
                holes.Append(Arc.Create(XYZ.Zero, 2, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
                holes.Append(Arc.Create(XYZ.Zero, 2, Math.PI, Math.PI * 2, XYZ.BasisX, XYZ.BasisY));
                doc.Create.NewRoomBoundaryLines(SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero)), holes, view);
                doc.Regenerate();
                room = doc.Create.NewRoom(level, new UV(5, 0));
                room.Name = "QA Круглое помещение";
                room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL).Set(level.Id);
                room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET).Set(10);
                tx.Commit();
            }
            string model = Path.Combine(root, "legacy-fixture.rvt");
            doc.SaveAs(model); doc.Close(false);
            UIDocument ui = app.OpenAndActivateDocument(model);
            doc = ui.Document;
            room = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Room>().Single();
            ui.Selection.SetElementIds(new[] { room.Id });
            var cases = new List<object>();
            foreach (FinishKind kind in new[] { FinishKind.Floors, FinishKind.Ceilings, FinishKind.Walls })
            {
                ui.Selection.SetElementIds(new[] { room.Id });
                using TransactionGroup scenario = new(doc, "QA " + kind); scenario.Start();
                FinishCatalog catalog = new(ui);
                FinishSettings settings = new() { Kind = kind, Scope = FinishScope.Selected, CeilingAsFloor = kind == FinishKind.Ceilings };
                FinishPlan plan = catalog.BuildPlan(settings);
                FinishTypeOption type = catalog.Types(settings).First();
                settings.Assignments = plan.Targets.Select(t => t.Key).Distinct().Select(k => new FinishAssignment { Key = k, TypeUniqueId = type.UniqueId }).ToList();
                FinishResult result = new FinishService(catalog).Create(settings);
                double[] areas = result.CreatedIds.Select(doc.GetElement).Select(e => e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0).ToArray();
                int repeat = new FinishService(new FinishCatalog(ui)).Create(settings).CreatedIds.Count;
                bool exactFloor = false;
                if (kind != FinishKind.Walls && result.CreatedIds.Count == 1)
                {
                    Element floor = doc.GetElement(result.CreatedIds[0]);
                    double expectedRadius = plan.Targets.Single().Loops.SelectMany(l => l).OfType<Arc>().Max(a => a.Radius);
                    Arc[] outer = floor.GetDependentElements(null).Select(doc.GetElement).OfType<Sketch>()
                        .SelectMany(s => s.Profile.Cast<CurveArray>()).SelectMany(a => a.Cast<Curve>()).OfType<Arc>()
                        .Where(a => Math.Abs(a.Radius - expectedRadius) < 1e-8).ToArray();
                    Opening[] holes = new FilteredElementCollector(doc).OfClass(typeof(Opening)).Cast<Opening>().Where(o => o.Host?.Id == floor.Id).ToArray();
                    Arc[] inner = holes.SelectMany(o => o.BoundaryCurves.Cast<Curve>()).OfType<Arc>().ToArray();
                    exactFloor = outer.Length == 2 && outer.All(a => Math.Abs(a.Radius - expectedRadius) < 1e-8)
                        && holes.Length == 1 && inner.Length == 2 && inner.All(a => Math.Abs(a.Radius - 2) < 1e-8);
                }
                bool geometry = kind == FinishKind.Walls
                    ? result.CreatedIds.Select(doc.GetElement).OfType<Wall>().All(w => ((LocationCurve)w.Location).Curve is Arc)
                    : exactFloor;
                bool passed = result.CreatedIds.Count == plan.Targets.Count && result.CreatedIds.Count > 0 && repeat == 0 && geometry;
                cases.Add(new { Case = "Legacy2021" + kind, Passed = passed, Created = result.CreatedIds.Count, Repeat = repeat, CurvedGeometry = geometry,
                    RoomArea = room.Area, Areas = areas, Targets = plan.Targets.Count, Rooms = plan.RoomCount,
                    Issues = result.Issues.Select(i => i.ToString()).ToArray() });
                scenario.RollBack();
            }
            File.WriteAllText(Path.Combine(root, "legacy.json"), new JavaScriptSerializer().Serialize(cases));
            File.WriteAllText(Path.Combine(root, "done"), "Complete");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root, "failed"), ex.ToString()); }
    }
}
