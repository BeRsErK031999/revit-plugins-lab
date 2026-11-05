using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.GeometricFinishes;

namespace TrueBIM.Revit.GeometricFinishesHarness;

internal static class FinishAcceptance
{
    public static void Run(UIApplication app, string root)
    {
        UIDocument ui = app.ActiveUIDocument;
        Document doc = ui.Document;
        var cases = new List<object>();
        foreach (FinishKind kind in new[] { FinishKind.Floors, FinishKind.Ceilings, FinishKind.Walls })
        {
            File.AppendAllText(Path.Combine(root, "checks.txt"), "Starting " + kind + Environment.NewLine);
            using TransactionGroup scenario = new(doc, "QA " + kind);
            scenario.Start();
            FinishCatalog catalog = new(ui);
            FinishSettings settings = Settings(kind, catalog);
            FinishPlan plan = catalog.BuildPlan(settings);
            FinishTypeOption type = catalog.Types(settings).Where(t => kind != FinishKind.Walls || t.ThicknessMm < 20)
                .OrderBy(t => Math.Abs(t.ThicknessMm - (kind == FinishKind.Walls ? 10 : kind == FinishKind.Floors ? 100 : 32))).First();
            settings.Assignments = plan.Targets.Select(t => t.Key).Distinct().Select(key => new FinishAssignment { Key = key, TypeUniqueId = type.UniqueId }).ToList();
            FinishResult result = new FinishService(catalog).Create(settings);
            int openings = result.CreatedIds.Select(doc.GetElement).OfType<Wall>().Sum(w => w.FindInserts(true, false, true, true).Count);
            bool parameters = result.CreatedIds.Select(doc.GetElement).All(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString()
                == ((Room)doc.GetElement(e.GetEntity(Autodesk.Revit.DB.ExtensibleStorage.Schema.Lookup(FinishOwnership.SchemaId)).Get<string>("RoomUniqueId"))).Number);
            bool boundingOff = result.CreatedIds.Select(doc.GetElement).All(e => e.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING)?.AsInteger() == 0);
            double[] areas = result.CreatedIds.Select(doc.GetElement).Select(e => e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0).ToArray();
            FinishResult repeat = new FinishService(new FinishCatalog(ui)).Create(settings);
            bool passed = result.CreatedRooms == plan.RoomCount && result.CreatedIds.Count == plan.Targets.Count
                && result.SkippedRooms == 0 && repeat.CreatedIds.Count == 0 && parameters && boundingOff && areas.All(a => a > 0);
            object outcome = new { Passed = passed, Kind = kind.ToString(), Type = type.Name, Rooms = plan.RoomCount, Targets = plan.Targets.Count,
                Created = result.CreatedIds.Count, result.CreatedRooms, result.SkippedRooms,
                Issues = result.Issues.Select(i => i.ToString()).ToArray(), Openings = openings,
                RepeatCreated = repeat.CreatedIds.Count, Parameters = parameters, BoundingOff = boundingOff,
                Areas = areas, PlanIssues = plan.Issues.Select(i => i.ToString()).ToArray() };
            cases.Add(outcome);
            File.WriteAllText(Path.Combine(root, "acceptance.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(cases));
            scenario.RollBack();
        }
        File.AppendAllText(Path.Combine(root, "checks.txt"), "Base acceptance finished" + Environment.NewLine);
    }
    internal static FinishSettings Settings(FinishKind kind, FinishCatalog catalog) => new()
    {
        Kind = kind, Scope = FinishScope.Project, Grouping = kind == FinishKind.Walls ? FinishGrouping.WallType : FinishGrouping.RoomName,
        Elevation = FinishElevation.RoomBoundary,
        RoomParameterKey = FinishParameters.BuiltIn(BuiltInParameter.ROOM_NUMBER),
        FinishParameterKey = FinishParameters.BuiltIn(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
    };
}
