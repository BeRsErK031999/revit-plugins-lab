using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.GeometricFinishes;

namespace TrueBIM.Revit.GeometricFinishesHarness;

internal static class FinishFixtureAcceptance
{
    public static void Run(UIApplication app, string root)
    {
        Document doc = app.ActiveUIDocument.Document;
        var checks = new List<object>();
        using TransactionGroup outer = new(doc, "QA isolated finish fixtures");
        outer.Start();
        try
        {
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => Math.Abs(l.Elevation)).First();
            Room curvedRoom = CreateCurvedRoom(app, level);
            foreach (FinishKind kind in new[] { FinishKind.Floors, FinishKind.Ceilings })
            {
                using TransactionGroup scenario = new(doc, "QA curved contour " + kind); scenario.Start();
                (FinishCatalog catalog, FinishSettings settings) = ForRoom(app.ActiveUIDocument, curvedRoom, kind);
                FinishResult result = new FinishService(catalog).Create(settings);
                double area = result.CreatedIds.Select(doc.GetElement).Sum(e => e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble());
                Element? made = result.CreatedIds.Count == 1 ? doc.GetElement(result.CreatedIds[0]) : null;
                Sketch? sketch = made == null ? null : doc.GetElement(made is Floor f ? f.SketchId : ((Ceiling)made).SketchId) as Sketch;
                Arc[] arcs = sketch?.Profile.Cast<CurveArray>().SelectMany(a => a.Cast<Curve>()).OfType<Arc>().ToArray() ?? Array.Empty<Arc>();
                // Revit's reported area tessellates curved boundaries; verify the exact sketch as well.
                bool passed = result.CreatedIds.Count == 1 && Math.Abs(area - (320 - Math.PI * 4)) < 0.01
                    && arcs.Length == 2 && arcs.All(a => Math.Abs(a.Radius - 2) < 1e-8);
                checks.Add(new { Case = "CurvedHole" + kind, Passed = passed, Created = result.CreatedIds.Count, Area = area,
                    ExpectedArea = 320 - Math.PI * 4, ArcRadii = arcs.Select(a => a.Radius).ToArray(), Issues = result.Issues.Select(i => i.ToString()).ToArray() });
                scenario.RollBack();
            }
            using (TransactionGroup scenario = new(doc, "QA bounding and offset"))
            {
                scenario.Start();
                (FinishCatalog catalog, FinishSettings settings) = ForRoom(app.ActiveUIDocument, curvedRoom, FinishKind.Floors);
                settings.Elevation = FinishElevation.Level; settings.OffsetMm = -125;
                settings.Assignments.ForEach(a => a.RoomBounding = true);
                FinishResult result = new FinishService(catalog).Create(settings);
                Element? floor = result.CreatedIds.Count == 1 ? doc.GetElement(result.CreatedIds[0]) : null;
                checks.Add(new { Case = "FloorOffsetAndRoomBounding", Passed = floor != null
                    && Math.Abs(floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).AsDouble() * 304.8 + 125) < 1e-5
                    && floor.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).AsInteger() == 1,
                    Issues = result.Issues.Select(i => i.ToString()).ToArray() });
                scenario.RollBack();
            }
            using (TransactionGroup scenario = new(doc, "QA ceiling floor"))
            {
                scenario.Start();
                (FinishCatalog catalog, FinishSettings settings) = ForRoom(app.ActiveUIDocument, curvedRoom, FinishKind.Ceilings, true);
                settings.Elevation = FinishElevation.Level; settings.OffsetMm = 2800;
                FinishResult result = new FinishService(catalog).Create(settings);
                Floor? floor = result.CreatedIds.Count == 1 ? doc.GetElement(result.CreatedIds[0]) as Floor : null;
                double bottom = floor == null ? double.NaN : floor.get_BoundingBox(null).Min.Z - level.Elevation;
                checks.Add(new { Case = "CeilingAsFloorBottomElevation", Passed = floor != null && Math.Abs(bottom * 304.8 - 2800) < 1e-5,
                    BottomMm = double.IsNaN(bottom) ? -1 : bottom * 304.8, Issues = result.Issues.Select(i => i.ToString()).ToArray() });
                scenario.RollBack();
            }
            using (TransactionGroup scenario = new(doc, "QA cancellation"))
            {
                scenario.Start();
                app.ActiveUIDocument.Selection.SetElementIds(new List<ElementId>());
                FinishCatalog catalog = new(app.ActiveUIDocument);
                FinishSettings settings = FinishAcceptance.Settings(FinishKind.Floors, catalog);
                Assign(catalog, settings);
                int before = new FilteredElementCollector(doc).OfClass(typeof(Floor)).GetElementCount();
                FinishResult result = new FinishService(catalog).Create(settings, (done, _) => done < 1);
                checks.Add(new { Case = "CancelRollsBackEntireBatch", Passed = result.Cancelled && result.CreatedIds.Count == 0
                    && new FilteredElementCollector(doc).OfClass(typeof(Floor)).GetElementCount() == before });
                scenario.RollBack();
            }
            using (TransactionGroup scenario = new(doc, "QA slab elevations"))
            {
                scenario.Start();
                FloorType type = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>()
                    .OrderBy(t => Math.Abs((t.GetCompoundStructure()?.GetWidth() ?? 0) * 304.8 - 100)).First();
                IList<CurveLoop> loops = curvedRoom.GetBoundarySegments(new SpatialElementBoundaryOptions())
                    .Select(s => FinishGeometry.ClosedRoomLoop(s, _ => { })).ToList();
                Floor lower, upper;
                using (Transaction tx = new(doc, "QA support slabs"))
                {
                    tx.Start();
                    lower = Floor.Create(doc, FinishGeometry.AtElevation(loops, level.Elevation), type.Id, level.Id);
                    lower.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(-100 * FinishGeometry.FeetPerMm);
                    upper = Floor.Create(doc, FinishGeometry.AtElevation(loops, level.Elevation), type.Id, level.Id);
                    upper.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM).Set(curvedRoom.UnboundedHeight);
                    tx.Commit();
                }
                foreach (FinishKind kind in new[] { FinishKind.Floors, FinishKind.Ceilings })
                {
                    (FinishCatalog catalog, FinishSettings settings) = ForRoom(app.ActiveUIDocument, curvedRoom, kind);
                    settings.Elevation = FinishElevation.Slab;
                    FinishResult result = new FinishService(catalog).Create(settings);
                    Element? made = result.CreatedIds.Count == 1 ? doc.GetElement(result.CreatedIds[0]) : null;
                    double gap = made == null ? -1 : kind == FinishKind.Floors
                        ? made.get_BoundingBox(null).Min.Z - lower.get_BoundingBox(null).Max.Z
                        : upper.get_BoundingBox(null).Min.Z - made.get_BoundingBox(null).Max.Z;
                    checks.Add(new { Case = "TouchSupportSlab" + kind, Passed = made != null && Math.Abs(gap) < 1e-6,
                        GapMm = gap * 304.8, Issues = result.Issues.Select(i => i.ToString()).ToArray() });
                }
                scenario.RollBack();
            }
            using (TransactionGroup scenario = new(doc, "QA bad parameter"))
            {
                scenario.Start();
                (FinishCatalog catalog, FinishSettings settings) = ForRoom(app.ActiveUIDocument, curvedRoom, FinishKind.Floors);
                settings.FinishParameterKey = "g:FA1F9F82-3B22-4F55-840E-C70EDFA89C88";
                int before = new FilteredElementCollector(doc).OfClass(typeof(Floor)).GetElementCount();
                FinishResult result = new FinishService(catalog).Create(settings);
                checks.Add(new { Case = "InvalidParameterRollsBackRoom", Passed = result.CreatedIds.Count == 0 && result.Issues.Count > 0
                    && new FilteredElementCollector(doc).OfClass(typeof(Floor)).GetElementCount() == before });
                scenario.RollBack();
            }
            using (TransactionGroup scenario = new(doc, "QA link placement"))
            {
                scenario.Start();
                Room linkedRoom = CreateLinkedRoom(app, root, level);
                (FinishCatalog catalog, FinishSettings settings) = ForRoom(app.ActiveUIDocument, linkedRoom, FinishKind.Walls);
                FinishPlan plan = catalog.BuildPlan(settings);
                FinishResult result = new FinishService(catalog).Create(settings);
                int openings = result.CreatedIds.Select(doc.GetElement).OfType<Wall>().Sum(w => w.FindInserts(true, false, true, true).Count);
                bool inHost = result.CreatedIds.All(id => doc.GetElement(id).Document.Equals(doc));
                bool height = result.CreatedIds.Select(doc.GetElement).All(e => Math.Abs(e.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM).AsDouble() - linkedRoom.UnboundedHeight) < 1e-6);
                bool passed = plan.Targets.Count == 4 && plan.Targets.All(t => t.Source?.Linked == true) && result.CreatedIds.Count == 4 && openings == 2 && inHost && height;
                checks.Add(new { Case = "RotatedTranslatedLinkedWallsAndOpenings", Passed = passed, Area = linkedRoom.Area,
                    Targets = plan.Targets.Count, Linked = plan.Targets.Count(t => t.Source?.Linked == true), Created = result.CreatedIds.Count,
                    Openings = openings, InHost = inHost, CorrectHeight = height, Issues = result.Issues.Select(i => i.ToString()).ToArray() });
                scenario.RollBack();
            }
        }
        finally
        {
            if (outer.GetStatus() == TransactionStatus.Started) outer.RollBack();
            app.ActiveUIDocument.Selection.SetElementIds(new List<ElementId>());
            File.WriteAllText(Path.Combine(root, "fixtures.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(checks));
        }
    }
    private static (FinishCatalog Catalog, FinishSettings Settings) ForRoom(UIDocument ui, Room room, FinishKind kind, bool ceilingAsFloor = false)
    {
        ui.Selection.SetElementIds(new[] { room.Id });
        FinishCatalog catalog = new(ui);
        FinishSettings settings = FinishAcceptance.Settings(kind, catalog);
        settings.Scope = FinishScope.Selected; settings.CeilingAsFloor = ceilingAsFloor;
        Assign(catalog, settings);
        return (catalog, settings);
    }
    private static void Assign(FinishCatalog catalog, FinishSettings settings)
    {
        FinishTypeOption type = catalog.Types(settings).Where(t => settings.Kind != FinishKind.Walls || t.ThicknessMm < 20)
            .OrderBy(t => Math.Abs(t.ThicknessMm - (settings.Kind == FinishKind.Walls ? 10 : settings.Kind == FinishKind.Ceilings && !settings.CeilingAsFloor ? 32 : 100))).First();
        settings.Assignments = catalog.BuildPlan(settings).Targets.Select(t => t.Key).Distinct()
            .Select(key => new FinishAssignment { Key = key, TypeUniqueId = type.UniqueId }).ToList();
    }
    private static Room CreateCurvedRoom(UIApplication app, Level level)
    {
        Document doc = app.ActiveUIDocument.Document;
        using Transaction tx = new(doc, "QA curved room with hole"); tx.Start();
        ViewFamilyType viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(v => v.ViewFamily == ViewFamily.FloorPlan);
        ViewPlan view = ViewPlan.Create(doc, viewType.Id, level.Id);
        SketchPlane plane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, new XYZ(0, 0, level.Elevation)));
        XYZ P(double x, double y) => new(500 + x, 300 + y, level.Elevation);
        CurveArray edges = new();
        XYZ[] points = { P(0, 0), P(20, 0), P(20, 16), P(0, 16) };
        for (int i = 0; i < 4; i++) edges.Append(Line.CreateBound(points[i], points[(i + 1) % 4]));
        edges.Append(Arc.Create(P(10, 8), 2, 0, Math.PI, XYZ.BasisX, XYZ.BasisY));
        edges.Append(Arc.Create(P(10, 8), 2, Math.PI, 2 * Math.PI, XYZ.BasisX, XYZ.BasisY));
        doc.Create.NewRoomBoundaryLines(plane, edges, view);
        doc.Regenerate();
        Room room = doc.Create.NewRoom(level, new UV(502, 302));
        room.Name = "QA Дуги и отверстие";
        room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL).Set(level.Id);
        room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET).Set(10);
        tx.Commit();
        return room;
    }
    private static Room CreateLinkedRoom(UIApplication app, string root, Level hostLevel)
    {
        string path = Path.Combine(root, "qa-linked-room.rvt");
        Document linked = app.Application.NewProjectDocument(UnitSystem.Metric);
        try
        {
            using (Transaction tx = new(linked, "QA link source"))
            {
                tx.Start();
                Level level = Level.Create(linked, 0);
                WallType type = new FilteredElementCollector(linked).OfClass(typeof(WallType)).Cast<WallType>().First(w => w.Kind == WallKind.Basic);
                XYZ[] points = { new(0, 0, 0), new(30, 0, 0), new(30, 20, 0), new(0, 20, 0) };
                List<Wall> walls = new();
                for (int i = 0; i < 4; i++) walls.Add(Wall.Create(linked, Line.CreateBound(points[i], points[(i + 1) % 4]), type.Id, level.Id, 12, 0, false, false));
                linked.Regenerate();
                linked.Create.NewOpening(walls[0], new XYZ(10, 0, 3), new XYZ(14, 0, 7));
                linked.Create.NewOpening(walls[3], new XYZ(0, 5, 0), new XYZ(0, 8, 7));
                tx.Commit();
            }
            linked.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        }
        finally { linked.Close(false); }
        Document doc = app.ActiveUIDocument.Document;
        using Transaction host = new(doc, "QA transformed link and room"); host.Start();
        LinkLoadResult load = RevitLinkType.Create(doc, ModelPathUtils.ConvertUserVisiblePathToModelPath(path), new RevitLinkOptions(false));
        doc.GetElement(load.ElementId).get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING).Set(1);
        RevitLinkInstance instance = RevitLinkInstance.Create(doc, load.ElementId);
        ElementTransformUtils.RotateElement(doc, instance.Id, Line.CreateBound(XYZ.Zero, XYZ.BasisZ), 0.37);
        ElementTransformUtils.MoveElement(doc, instance.Id, new XYZ(350, 250, hostLevel.Elevation));
        doc.Regenerate();
        XYZ point = instance.GetTotalTransform().OfPoint(new XYZ(15, 10, 0));
        Room room = doc.Create.NewRoom(hostLevel, new UV(point.X, point.Y));
        room.Name = "QA Стены из связи";
        room.get_Parameter(BuiltInParameter.ROOM_UPPER_LEVEL).Set(hostLevel.Id);
        room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET).Set(10);
        host.Commit();
        return room;
    }
}
