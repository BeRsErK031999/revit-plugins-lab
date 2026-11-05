using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;

namespace TrueBIM.Revit.GeometricFinishesHarness;

/// <summary>Opt-in native audit and acceptance on disposable copies only.</summary>
public sealed class FinishesHarnessApplication : IExternalApplication
{
    private string? root;
    private bool ran;
    public Result OnStartup(UIControlledApplication app)
    {
        root = Environment.GetEnvironmentVariable("TRUEBIM_FINISHES_QA");
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
            string[] models = Directory.GetFiles(Path.Combine(root!, "models"), "*.rvt");
            string path = models.Single(p => Path.GetFileName(p).Contains("AR6"));
            foreach (string model in models)
            {
                ModelPath mp = ModelPathUtils.ConvertUserVisiblePathToModelPath(model);
                TransmissionData? td = TransmissionData.ReadTransmissionData(mp);
                if (td == null) continue;
                foreach (ElementId id in td.GetAllExternalFileReferenceIds())
                {
                    ExternalFileReference er = td.GetLastSavedReferenceData(id);
                    if (er.ExternalFileReferenceType != ExternalFileReferenceType.RevitLink) continue;
                    string old = ModelPathUtils.ConvertModelPathToUserVisiblePath(er.GetPath());
                    string? found = models.FirstOrDefault(m => Path.GetFileName(m).Equals(Path.GetFileName(old), StringComparison.OrdinalIgnoreCase));
                    td.SetDesiredReferenceData(id, found == null ? er.GetPath() : ModelPathUtils.ConvertUserVisiblePathToModelPath(found),
                        found == null ? er.PathType : PathType.Absolute, found != null);
                }
                td.IsTransmitted = true;
                TransmissionData.WriteTransmissionData(mp, td);
            }
            using OpenOptions options = new() { DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets };
            Document doc = app.OpenAndActivateDocument(ModelPathUtils.ConvertUserVisiblePathToModelPath(path), options, false).Document;
            var rooms = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).OfType<Room>().ToList();
            var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>().ToList();
            object report = new
            {
                Title = doc.Title, Workshared = doc.IsWorkshared,
                Rooms = rooms.Select(r => new {
                    Id = r.Id.Value, r.Number, r.Name, Area = r.Area * 0.09290304, Height = r.UnboundedHeight * 304.8,
                    Level = doc.GetElement(r.LevelId)?.Name,
                    Boundaries = (r.GetBoundarySegments(new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish }) ?? new List<IList<BoundarySegment>>())
                        .Select(loop => loop.Select(b => new { Host = b.ElementId.Value, Link = b.LinkElementId.Value,
                            Type = b.GetCurve().GetType().Name, Length = b.GetCurve().Length * 304.8 }).ToArray()).ToArray()
                }).ToArray(),
                Links = links.Select(l => new { Id = l.Id.Value, l.Name, Loaded = l.GetLinkDocument() != null,
                    Walls = l.GetLinkDocument() == null ? 0 : new FilteredElementCollector(l.GetLinkDocument()).OfClass(typeof(Wall)).GetElementCount() }).ToArray(),
                Types = new FilteredElementCollector(doc).WhereElementIsElementType().Where(t => t is WallType || t is FloorType || t is CeilingType)
                    .Select(t => new { Id = t.Id.Value, t.Name, Category = t.Category?.Name,
                        Width = t is HostObjAttributes h ? (h.GetCompoundStructure()?.GetWidth() ?? 0) * 304.8 : 0 }).ToArray(),
                Elements = new { Walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount(),
                    Floors = new FilteredElementCollector(doc).OfClass(typeof(Floor)).GetElementCount(),
                    Ceilings = new FilteredElementCollector(doc).OfClass(typeof(Ceiling)).GetElementCount() },
                Inserts = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                    .Where(f => f.Host is Wall).Take(8).Select(f => new { Id = f.Id.Value, f.Name, Host = f.Host.Id.Value,
                        Box = Box(f.get_BoundingBox(null)), Point = f.Location is LocationPoint p ? new[] {p.Point.X,p.Point.Y,p.Point.Z} : null,
                        Parameters = f.Parameters.Cast<Parameter>().Where(p => p.HasValue && (p.Definition.Name.Contains("Высот") || p.Definition.Name.Contains("Ширин") || p.Definition.Name.Contains("Отмет") || p.Definition.Name.Contains("Смещен")))
                            .Select(p => new { p.Definition.Name, Value = p.AsValueString() ?? p.AsString() }).ToArray()
                    }).ToArray()
            };
            File.WriteAllText(Path.Combine(root!, "audit.json"), new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(report));
            FinishGeometryAudit.Run(doc, root!);
            if (Environment.GetEnvironmentVariable("TRUEBIM_FINISHES_MODE") != "audit")
            {
                FinishUiAcceptance.Run(app, root!);
                if (Environment.GetEnvironmentVariable("TRUEBIM_FINISHES_MODE") != "ui")
                {
                    FinishAcceptance.Run(app, root!);
                    FinishFixtureAcceptance.Run(app, root!);
                }
            }
            File.WriteAllText(Path.Combine(root!, "done"), "Audit complete");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(root!, "failed"), ex.ToString()); }
    }
    private static double[]? Box(BoundingBoxXYZ? b) => b == null ? null : new[] { b.Min.X, b.Min.Y, b.Min.Z, b.Max.X, b.Max.Y, b.Max.Z };
}
