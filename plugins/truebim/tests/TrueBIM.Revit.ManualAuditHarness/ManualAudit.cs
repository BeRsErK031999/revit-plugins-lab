using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.ManualAuditHarness;

/// <summary>Диагностика фактически загруженной TrueBIM.App; исходники расчёта в harness не включаются.</summary>
public static class ManualAudit
{
    private static readonly JavaScriptSerializer Json = new() { MaxJsonLength = int.MaxValue, RecursionLimit = 120 };
    private const double SquareMeters = 0.09290304;
    private static System.Threading.Timer? timer;
    private static ExternalEvent? externalEvent;
    private static string? lastRequest;
    private static int queued;

    public static string Start(UIApplication application, string directory)
    {
        timer?.Dispose();
        lastRequest = null;
        externalEvent = ExternalEvent.Create(new AuditEventHandler(directory));
        timer = new System.Threading.Timer(_ =>
        {
            try
            {
                string requestPath = Path.Combine(directory, "request.json");
                if (!File.Exists(requestPath) || File.ReadAllText(requestPath) == lastRequest) return;
                if (System.Threading.Interlocked.Exchange(ref queued, 1) == 0) externalEvent.Raise();
            }
            catch (IOException) { System.Threading.Interlocked.Exchange(ref queued, 0); }
        }, null, 500, 500);
        return "Manual audit attached to the current Revit session via ExternalEvent.";
    }

    private sealed class AuditEventHandler(string directory) : IExternalEventHandler
    {
        public string GetName() => "TrueBIM manual audit";

        public void Execute(UIApplication application)
        {
            try
            {
                lastRequest = File.ReadAllText(Path.Combine(directory, "request.json"));
                if (Json.Deserialize<Dictionary<string, object>>(lastRequest)["Operation"].Equals("Stop"))
                {
                    timer?.Dispose();
                    timer = null;
                    File.WriteAllText(Path.Combine(directory, "stopped.txt"), DateTime.UtcNow.ToString("o"));
                    return;
                }
                string result = Run(application.ActiveUIDocument, directory);
                File.WriteAllText(Path.Combine(directory, "last-result.txt"), result);
            }
            catch (Exception exception) { File.WriteAllText(Path.Combine(directory, "last-error.txt"), exception.ToString()); }
            finally { System.Threading.Interlocked.Exchange(ref queued, 0); }
        }
    }

    public static string Run(UIDocument uiDocument, string directory)
    {
        Directory.CreateDirectory(directory);
        Dictionary<string, object> request = Json.Deserialize<Dictionary<string, object>>(
            File.ReadAllText(Path.Combine(directory, "request.json")));
        string operation = (string)request["Operation"];
        string name = (string)request["Name"];
        string path = Path.Combine(directory, name + ".json");
        Document document = uiDocument.Document;
        try
        {
            object result = operation switch
            {
                "Inventory" => Inventory(document),
                "Calculate" => Calculate(document, path),
                "SaveCopy" => SaveCopy(document, (string)request["Path"]),
                "Inspect" => Inspect(uiDocument, request),
                "Geometry" => CaptureGeometry(document, request),
                _ => throw new ArgumentException("Unknown manual audit operation: " + operation)
            };
            File.WriteAllText(path, Json.Serialize(result));
            return "OK: " + path;
        }
        catch (Exception exception)
        {
            File.WriteAllText(path + ".error.txt", exception.ToString());
            throw;
        }
    }

    private static object Inventory(Document document)
    {
        AuditLogger logger = new(null);
        FinishScheduleSettings settings = Settings(document, logger);
        FinishElementCollection collection = new FinishElementCollector(logger).Collect(document, settings);
        FinishClassificationResult classification = new FinishClassificationService().Classify(collection, settings);
        using SpatialElementBoundaryOptions boundary = new()
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        };
        return new
        {
            CreatedUtc = DateTime.UtcNow.ToString("o"), Document = DocumentInfo(document), Assembly = AssemblyInfo(),
            Settings = settings,
            Classified = classification.Elements.Select(item => new { item.Element.ElementId, Category = item.Category.ToString() }),
            Skipped = classification.SkippedElements,
            Elements = collection.Walls.Concat(collection.Floors).Concat(collection.Ceilings)
                .Select(item => CaptureElement(document.GetElement(new ElementId((int)item.ElementId)))).ToArray(),
            Rooms = collection.Rooms.Select(item =>
            {
                Room room = (Room)document.GetElement(new ElementId((int)item.ElementId));
                return new
                {
                    item.ElementId, item.Number, item.Name, item.LevelId, item.Bounds, item.HasLocation,
                    Area = room.Area * SquareMeters,
                    Location = room.Location is LocationPoint point ? Point(point.Point) : null,
                    Parameters = Parameters(room),
                    Footprints = room.GetBoundarySegments(boundary)?.Select(loop => loop.Select(segment => new
                    {
                        ElementId = segment.ElementId.IntegerValue,
                        Points = segment.GetCurve().Tessellate().Select(Point).ToArray()
                    }).ToArray()).ToArray()
                };
            }).ToArray(),
            Levels = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .Select(level => new { Id = level.Id.IntegerValue, level.Name, level.Elevation }).ToArray(),
            Plans = new FilteredElementCollector(document).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(view => !view.IsTemplate).Select(view => new
                {
                    Id = view.Id.IntegerValue, view.Name, LevelId = view.GenLevel?.Id.IntegerValue,
                    Type = view.ViewType.ToString()
                }).ToArray(),
            Schedules = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .Where(view => !view.IsTemplate && (view.Name == "Спецификация стен" || view.Name == "Спецификация перекрытий"
                    || view.Name == "Спецификация потолков" || view.Name.Contains("Ведомость отделки помещений")))
                .Select(CaptureSchedule).ToArray()
        };
    }

    private static object Calculate(Document document, string path)
    {
        AuditLogger logger = new(path + ".progress.txt");
        FinishScheduleSettings settings = Settings(document, logger);
        FinishSchedulePreviewService service = new(new FinishElementCollector(logger),
            new FinishSchedulePreviewBuilder(new RoomScopeService(), new FinishClassificationService()), logger);
        bool modified = document.IsModified;
        FinishScheduleCalculationResult calculation = service.BuildDetailed(document, settings);
        return new
        {
            CreatedUtc = DateTime.UtcNow.ToString("o"), Document = DocumentInfo(document), Assembly = AssemblyInfo(),
            ModifiedBefore = modified, ModifiedAfter = document.IsModified, Settings = settings,
            calculation.Preview, calculation.Quantities, calculation.RoomSnapshots,
            Groups = calculation.Aggregation?.Groups,
            Classified = calculation.Build.Classification.Elements.Select(item => new { item.Element.ElementId, Category = item.Category.ToString() }),
            InScope = calculation.Build.InScopeElements.Select(item => item.Element.ElementId).ToArray()
        };
    }

    private static FinishScheduleSettings Settings(Document document, AuditLogger logger)
    {
        FinishScheduleSettings settings = new FinishScheduleProfileStorage(logger).Load();
        return new FinishSchedulePreferredParameterResolver(new FinishScheduleParameterOptionService(new ParameterCatalogMatcher())).Resolve(
            settings, new ParameterCatalogService(logger).Collect(document), ParameterCatalogService.TargetCategories);
    }

    private static object SaveCopy(Document document, string path)
    {
        if (File.Exists(path)) throw new InvalidOperationException("The audit copy already exists.");
        if (!Path.GetFileName(path).StartsWith("FinishSchedule_ManualAudit_", StringComparison.Ordinal))
            throw new InvalidOperationException("An explicit audit copy name is required.");
        object before = DocumentInfo(document);
        using SaveAsOptions options = new() { OverwriteExistingFile = false, MaximumBackups = 1 };
        if (document.IsWorkshared)
        {
            using WorksharingSaveAsOptions worksharing = new() { SaveAsCentral = true };
            options.SetWorksharingOptions(worksharing);
        }
        document.SaveAs(path, options);
        return new { Before = before, After = DocumentInfo(document), SyncWithCentral = false };
    }

    private static object Inspect(UIDocument uiDocument, Dictionary<string, object> request)
    {
        int[] ids = ((System.Collections.IEnumerable)request["ElementIds"]).Cast<object>().Select(Convert.ToInt32).ToArray();
        Document document = uiDocument.Document;
        if (request.TryGetValue("ViewId", out object viewId))
            uiDocument.ActiveView = (View)document.GetElement(new ElementId(Convert.ToInt32(viewId)));
        if (request.TryGetValue("CutOffset", out object cutOffset))
        {
            if (!Path.GetFileName(document.PathName).StartsWith("FinishSchedule_ManualAudit_", StringComparison.Ordinal))
                throw new InvalidOperationException("Inspection views may only be created in the explicit audit copy.");
            ViewPlan source = (ViewPlan)uiDocument.ActiveView;
            using Transaction transaction = new(document, "TrueBIM audit inspection view");
            transaction.Start();
            ViewPlan plan = (ViewPlan)document.GetElement(source.Duplicate(ViewDuplicateOption.WithDetailing));
            plan.Name = "Audit inspection " + request["Name"];
            plan.ViewTemplateId = ElementId.InvalidElementId;
            plan.CropBoxActive = false;
            double height = Convert.ToDouble(cutOffset);
            using PlanViewRange range = plan.GetViewRange();
            foreach (PlanViewPlane plane in new[] { PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane,
                         PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane })
                range.SetLevelId(plane, source.GenLevel.Id);
            range.SetOffset(PlanViewPlane.TopClipPlane, height + 2);
            range.SetOffset(PlanViewPlane.CutPlane, height);
            range.SetOffset(PlanViewPlane.BottomClipPlane, Math.Min(-2, height - 2));
            range.SetOffset(PlanViewPlane.ViewDepthPlane, Math.Min(-2, height - 2));
            plan.SetViewRange(range);
            transaction.Commit();
            uiDocument.ActiveView = plan;
        }
        List<ElementId> selection = ids.Select(id => new ElementId(id)).ToList();
        uiDocument.Selection.SetElementIds(selection);
        if (request.ContainsKey("ZoomOnly") || request.ContainsKey("CutOffset"))
        {
            BoundingBoxXYZ[] bounds = ids.Select(id => document.GetElement(new ElementId(id)).get_BoundingBox(null))
                .Where(bounds => bounds is not null).ToArray();
            XYZ low = new(bounds.Min(box => box.Min.X) - 2, bounds.Min(box => box.Min.Y) - 2, bounds.Min(box => box.Min.Z));
            XYZ high = new(bounds.Max(box => box.Max.X) + 2, bounds.Max(box => box.Max.Y) + 2, bounds.Max(box => box.Max.Z));
            uiDocument.GetOpenUIViews().First(view => view.ViewId == uiDocument.ActiveView.Id).ZoomAndCenterRectangle(low, high);
        }
        else uiDocument.ShowElements(selection);
        uiDocument.RefreshActiveView();
        return new { ViewId = uiDocument.ActiveView.Id.IntegerValue, Elements = ids.Select(id => CaptureElement(document.GetElement(new ElementId(id)))).ToArray() };
    }

    private static object CaptureGeometry(Document document, Dictionary<string, object> request)
    {
        int[] ids = ((System.Collections.IEnumerable)request["ElementIds"]).Cast<object>().Select(Convert.ToInt32).ToArray();
        using Options options = new() { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true };
        return ids.Select(id =>
        {
            Element element = document.GetElement(new ElementId(id));
            Solid[] solids = Solids(element.get_Geometry(options)).ToArray();
            return new
            {
                ElementId = id,
                Solids = solids.Select(solid => new
                {
                    solid.Volume,
                    Faces = solid.Faces.Cast<Face>().Select(face => new
                    {
                        Area = face.Area * SquareMeters,
                        Normal = face is PlanarFace plane ? Point(plane.FaceNormal) : null,
                        Loops = face.GetEdgesAsCurveLoops().Select(loop => loop.SelectMany(curve => curve.Tessellate().Take(curve.Tessellate().Count - 1)).Select(Point).ToArray()).ToArray()
                    }).ToArray()
                }).ToArray()
            };
        }).ToArray();
    }

    private static IEnumerable<Solid> Solids(GeometryElement geometry)
    {
        foreach (GeometryObject item in geometry)
        {
            if (item is Solid solid && solid.Volume > 1e-10) yield return solid;
            if (item is GeometryInstance instance)
                foreach (Solid nested in Solids(instance.GetInstanceGeometry())) yield return nested;
        }
    }

    private static object DocumentInfo(Document document) => new
    {
        document.Title, document.PathName, document.IsModified, document.IsWorkshared, document.IsDetached,
        CentralPath = document.IsWorkshared && document.GetWorksharingCentralModelPath() is { } central
            ? ModelPathUtils.ConvertModelPathToUserVisiblePath(central) : null,
        RevitVersion = document.Application.VersionNumber, RevitBuild = document.Application.VersionBuild
    };

    private static object AssemblyInfo()
    {
        Assembly assembly = typeof(FinishSchedulePreviewService).Assembly;
        using SHA256 sha = SHA256.Create();
        return new
        {
            assembly.FullName, assembly.Location, Mvid = assembly.ManifestModule.ModuleVersionId,
            Sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assembly.Location))).Replace("-", ""),
            Timestamp = File.GetLastWriteTime(assembly.Location).ToString("o"),
            FileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion,
            ProductVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion
        };
    }

    private static object CaptureElement(Element element)
    {
        Element? type = element.Document.GetElement(element.GetTypeId());
        BoundingBoxXYZ? bounds = element.get_BoundingBox(null);
        return new
        {
            ElementId = element.Id.IntegerValue, TypeId = element.GetTypeId().IntegerValue,
            Category = element.Category?.Name, CategoryId = element.Category?.Id.IntegerValue,
            TypeName = type?.Name, LevelId = element.LevelId.IntegerValue,
            Bounds = bounds is null ? null : new { Min = Point(bounds.Min), Max = Point(bounds.Max) },
            Area = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * SquareMeters,
            AreaDisplay = element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsValueString(),
            Parameters = Parameters(element), TypeParameters = type is null ? null : Parameters(type),
            Location = element.Location is LocationCurve curve ? curve.Curve.Tessellate().Select(Point).ToArray() : null
        };
    }

    private static object[] Parameters(Element element) => element.Parameters.Cast<Parameter>()
        .Select(parameter => (object)new
        {
            Id = parameter.Id.IntegerValue, Name = parameter.Definition.Name, StorageType = parameter.StorageType.ToString(),
            Value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString(),
            Raw = parameter.StorageType == StorageType.Double ? (double?)parameter.AsDouble() : null
        }).ToArray();

    private static object CaptureSchedule(ViewSchedule schedule)
    {
        ScheduleDefinition definition = schedule.Definition;
        using TableData table = schedule.GetTableData();
        using TableSectionData body = table.GetSectionData(SectionType.Body);
        return new
        {
            Id = schedule.Id.IntegerValue, schedule.Name, CategoryId = definition.CategoryId.IntegerValue,
            definition.IsItemized, definition.IncludeLinkedFiles, definition.ShowGrandTotal,
            Fields = definition.GetFieldOrder().Select(id =>
            {
                ScheduleField field = definition.GetField(id);
                return new { Id = id.IntegerValue, Name = field.GetName(), field.ColumnHeading, field.IsHidden,
                    ParameterId = field.ParameterId.IntegerValue, field.IsCalculatedField, DisplayType = field.DisplayType.ToString() };
            }).ToArray(),
            Filters = definition.GetFilters().Select(filter => new
            {
                FieldId = filter.FieldId.IntegerValue, Type = filter.FilterType.ToString(),
                Value = filter.IsStringValue ? filter.GetStringValue() : filter.IsIntegerValue ? filter.GetIntegerValue().ToString()
                    : filter.IsElementIdValue ? filter.GetElementIdValue().IntegerValue.ToString() : filter.IsDoubleValue ? filter.GetDoubleValue().ToString("R") : null
            }).ToArray(),
            Sorting = definition.GetSortGroupFields().Select(sort => new { FieldId = sort.FieldId.IntegerValue, Order = sort.SortOrder.ToString(), sort.ShowHeader, sort.ShowFooter, sort.ShowBlankLine }).ToArray(),
            ElementIds = new FilteredElementCollector(schedule.Document, schedule.Id).WhereElementIsNotElementType().ToElementIds().Select(id => id.IntegerValue).OrderBy(id => id).ToArray(),
            Rows = Enumerable.Range(body.FirstRowNumber, body.NumberOfRows).Select(row =>
                Enumerable.Range(body.FirstColumnNumber, body.NumberOfColumns).Select(column => schedule.GetCellText(SectionType.Body, row, column)).ToArray()).ToArray()
        };
    }

    private static double[] Point(XYZ point) => [point.X, point.Y, point.Z];

    private sealed class AuditLogger(string? path) : ITrueBimLogger
    {
        public void Info(string message) { if (path is not null) File.AppendAllText(path, message + Environment.NewLine); }
        public void Warning(string message) => Info(message);
        public void Error(string message, Exception? exception = null) => Info(message + " " + exception);
    }
}
