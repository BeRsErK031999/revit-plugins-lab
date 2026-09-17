using System.IO;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Modules.FinishSchedule.Models;
using TrueBIM.App.Modules.FinishSchedule.Revit;
using TrueBIM.App.Modules.FinishSchedule.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.Revit.FinishScheduleHarness;

/// <summary>Выгружает две указанные пользователем ведомости и исходные данные без записи в модель.</summary>
public static class ScheduleComparisonAudit
{
    private const string ReferenceName = "Помещения • Ведомость отделки помещений";
    private const string GeneratedName = "Помещения • Ведомость отделки помещений 12357756";

    public static string Run(Document document, string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        JavaScriptSerializer serializer = new() { MaxJsonLength = int.MaxValue, RecursionLimit = 100 };
        ViewSchedule[] schedules = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>().Where(view => view.Name == ReferenceName || view.Name == GeneratedName).ToArray();
        if (schedules.Length != 2)
            throw new InvalidOperationException("Обе заданные ведомости должны присутствовать в открытом документе.");
        bool modifiedBefore = document.IsModified;
        string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TrueBIM", "BimTools", "finish-schedule", "settings.json");
        string profileJson = File.ReadAllText(settingsPath);
        Dictionary<string, object> profile = serializer.Deserialize<Dictionary<string, object>>(profileJson);
        FinishScheduleSettings settings = ReadSettings(profile);
        AuditLogger logger = new(reportPath + ".progress.txt");
        FinishSchedulePreviewService service = new(new FinishElementCollector(logger),
            new FinishSchedulePreviewBuilder(new RoomScopeService(), new FinishClassificationService()), logger);
        FinishScheduleCalculationResult calculation = service.BuildDetailed(document, settings);
        FinishElementGeometryCache geometry = new(document);
        using SpatialElementBoundaryOptions boundaryOptions = new()
        {
            SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
        };
        object report = new
        {
            Document = new { document.Title, document.PathName, ModifiedBefore = modifiedBefore, document.IsModified },
            CreatedUtc = DateTime.UtcNow.ToString("o"),
            Profile = profile,
            Settings = settings,
            Schedules = schedules.Select(CaptureSchedule).ToArray(),
            Calculation = new
            {
                calculation.Quantities,
                calculation.RoomSnapshots,
                Groups = calculation.Aggregation?.Groups,
                ClassifiedElements = calculation.Build.Classification.Elements,
                calculation.Preview.Performance
            },
            Rooms = calculation.Collection.Rooms.Select(candidate =>
            {
                Room room = (Room)document.GetElement(new ElementId((int)candidate.ElementId));
                return new
                {
                    candidate.ElementId, candidate.Number, candidate.Name, candidate.LevelId, candidate.Bounds,
                    AreaSquareMeters = room.Area * 0.09290304,
                    Parameters = ReadParameters(room),
                    Footprints = room.GetBoundarySegments(boundaryOptions)?.Select(loop => loop.Select(segment => new
                    {
                        ElementId = segment.ElementId.IntegerValue,
                        Points = segment.GetCurve().Tessellate().Select(Point).ToArray()
                    }).ToArray()).ToArray()
                };
            }).ToArray(),
            Elements = calculation.Collection.Walls.Concat(calculation.Collection.Floors).Concat(calculation.Collection.Ceilings)
                .Select(candidate =>
                {
                    Element element = document.GetElement(new ElementId((int)candidate.ElementId));
                    Element type = document.GetElement(element.GetTypeId());
                    return new
                    {
                        candidate.ElementId, candidate.TypeId, Category = candidate.PhysicalCategory.ToString(),
                        TypeName = type.Name, LevelId = element.LevelId.IntegerValue, candidate.Bounds,
                        FullAreaSquareMeters = (element.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0) * 0.09290304,
                        Parameters = ReadParameters(element), TypeParameters = ReadParameters(type),
                        WallWidth = element is Wall wall ? wall.Width : (double?)null,
                        Location = element.Location is LocationCurve curve ? curve.Curve.Tessellate().Select(Point).ToArray() : null
                    };
                }).ToArray(),
            HorizontalFaces = calculation.Build.Classification.Elements
                .Where(candidate => candidate.Category is FinishPreviewCategory.Ceilings or FinishPreviewCategory.Floors)
                .Select(candidate => new
                {
                    ElementId = candidate.Element.ElementId,
                    Faces = geometry.Get(candidate.Element.ElementId).Geometry?.Solids
                        .SelectMany(solid => solid.Faces.OfType<PlanarFace>())
                        .Where(face => Math.Abs(face.FaceNormal.Z) > 0.1).Select(face => new
                        {
                            AreaSquareMeters = face.Area * 0.09290304, Normal = Point(face.FaceNormal),
                            Loops = face.GetEdgesAsCurveLoops().Select(loop => loop
                                .SelectMany(curve => curve.Tessellate().Select(Point)).ToArray()).ToArray()
                        }).ToArray()
                }).ToArray()
        };
        File.WriteAllText(reportPath, serializer.Serialize(report));
        string? exportError = null;
        using (TransactionGroup rollback = new(document, "TrueBIM: временные листы для сравнения ведомостей"))
        {
            rollback.Start();
            try
            {
                foreach (ViewSchedule schedule in schedules)
                {
                    ViewSheet sheet;
                    using (Transaction transaction = new(document, "Временный лист сравнения"))
                    {
                        transaction.Start();
                        sheet = ViewSheet.Create(document, ElementId.InvalidElementId);
                        sheet.Name = schedule.Name == ReferenceName ? "Эталонная ведомость" : "Ведомость плагина";
                        ScheduleSheetInstance.Create(document, sheet.Id, schedule.Id, XYZ.Zero);
                        transaction.Commit();
                    }
                    using ImageExportOptions options = new()
                    {
                        FilePath = reportPath + ".sheet", ExportRange = ExportRange.SetOfViews,
                        HLRandWFViewsFileType = ImageFileType.PNG, ShadowViewsFileType = ImageFileType.PNG,
                        ImageResolution = ImageResolution.DPI_150, ZoomType = ZoomFitType.FitToPage, PixelSize = 9000
                    };
                    options.SetViewsAndSheets(new List<ElementId> { sheet.Id });
                    document.ExportImage(options);
                }
            }
            catch (Exception exception)
            {
                exportError = exception.ToString();
            }
            finally
            {
                rollback.RollBack();
            }
        }
        string result = serializer.Serialize(new { ExportError = exportError, Report = reportPath, TemporarySheetsRolledBack = true });
        File.WriteAllText(reportPath + ".result.json", result);
        return result;
    }

    private static object CaptureSchedule(ViewSchedule schedule)
    {
        schedule.RefreshData();
        FinishScheduleMetadataService metadata = new();
        using TableData table = schedule.GetTableData();
        object[] sections = new[] { SectionType.Header, SectionType.Body }.Select(type =>
        {
            using TableSectionData section = table.GetSectionData(type);
            return (object)new
            {
                Type = type.ToString(), section.FirstRowNumber, section.FirstColumnNumber,
                WidthsMillimeters = Enumerable.Range(section.FirstColumnNumber, section.NumberOfColumns)
                    .Select(column => section.GetColumnWidth(column) * 304.8).ToArray(),
                Rows = Enumerable.Range(section.FirstRowNumber, section.NumberOfRows).Select(row => new
                {
                    Row = row, HeightMillimeters = section.GetRowHeight(row) * 304.8,
                    Cells = Enumerable.Range(section.FirstColumnNumber, section.NumberOfColumns).Select(column =>
                    {
                        TableMergedCell merged = section.GetMergedCell(row, column);
                        using TableCellStyle style = section.GetTableCellStyle(row, column);
                        return new
                        {
                            Column = column,
                            Text = merged.Top == row && merged.Left == column ? schedule.GetCellText(type, row, column) : string.Empty,
                            Merge = new { merged.Top, merged.Left, merged.Bottom, merged.Right },
                            style.FontName, style.TextSize
                        };
                    }).ToArray()
                }).ToArray()
            };
        }).ToArray();
        return new
        {
            Id = schedule.Id.IntegerValue, schedule.Name, Managed = metadata.IsManaged(schedule), Snapshot = metadata.IsSnapshot(schedule),
            schedule.Definition.ShowTitle, schedule.Definition.ShowHeaders, Sections = sections,
            Fields = schedule.Definition.GetFieldOrder().Select(id =>
            {
                ScheduleField field = schedule.Definition.GetField(id);
                return new { Name = field.GetName(), field.ColumnHeading, ParameterId = field.ParameterId.IntegerValue, field.IsHidden };
            }).ToArray()
        };
    }

    private static object[] ReadParameters(Element element) => element.Parameters.Cast<Parameter>()
        .Where(parameter => parameter.StorageType == StorageType.String)
        .Select(parameter => (object)new { Name = parameter.Definition.Name, Value = parameter.AsString() }).ToArray();

    private static double[] Point(XYZ point) => [point.X, point.Y, point.Z];

    private static FinishScheduleSettings ReadSettings(Dictionary<string, object> profile) => new(
        Reference(profile, "DescriptionParameter"),
        new RoomIdentifierSettings(ParseEnum<RoomIdentifierMode>(profile, "RoomIdentifierMode"), Reference(profile, "RoomIdentifierParameter")),
        Convert.ToBoolean(profile["WriteOwnership"]),
        Category(profile, "Walls"), Category(profile, "Floors"), Category(profile, "Ceilings"),
        Reference(profile, "RoomListOutputParameter"),
        new ReportScopeSettings(ParseEnum<ReportScopeKind>(profile, "ScopeKind"),
            profile.TryGetValue("LevelId", out object? level) ? Convert.ToInt64(level) : null,
            Reference(profile, "SectionParameter"), Convert.ToString(profile["SectionValue"]) ?? string.Empty),
        Convert.ToString(profile["ScheduleName"]) ?? GeneratedName,
        new FinishScheduleColumnWidths(Convert.ToDouble(profile["RoomListColumnWidthMillimeters"]),
            Convert.ToDouble(profile["DescriptionColumnWidthMillimeters"]), Convert.ToDouble(profile["AreaColumnWidthMillimeters"])));

    private static FinishCategorySettings Category(Dictionary<string, object> profile, string key)
    {
        Dictionary<string, object> data = (Dictionary<string, object>)profile[key];
        return new FinishCategorySettings(Convert.ToBoolean(data["IsEnabled"]), Convert.ToString(data["ClassificationValue"])!,
            Reference(data, "OwnershipParameter"), Reference(data, "OutputDescriptionParameter"), Reference(data, "OutputAreaParameter"));
    }

    private static ParameterReference? Reference(Dictionary<string, object> profile, string key)
    {
        if (!profile.TryGetValue(key, out object? value) || value is not Dictionary<string, object> data) return null;
        string name = Convert.ToString(data["Name"])!;
        ParameterBindingKind binding = ParseEnum<ParameterBindingKind>(data, "BindingKind");
        ParameterStorageKind storage = ParseEnum<ParameterStorageKind>(data, "StorageKind");
        return ParseEnum<ParameterIdentityKind>(data, "IdentityKind") switch
        {
            ParameterIdentityKind.BuiltIn => ParameterReference.BuiltIn(name, Convert.ToInt64(data["BuiltInParameterId"]), binding, storage),
            ParameterIdentityKind.Shared => ParameterReference.Shared(name, Guid.Parse(Convert.ToString(data["SharedParameterGuid"])!),
                Convert.ToInt64(data["DefinitionElementId"]), binding, storage),
            _ => ParameterReference.Project(name, Convert.ToInt64(data["DefinitionElementId"]), binding, storage)
        };
    }

    private static T ParseEnum<T>(Dictionary<string, object> data, string key) where T : struct =>
        (T)Enum.Parse(typeof(T), Convert.ToString(data[key])!);

    private sealed class AuditLogger(string path) : ITrueBimLogger
    {
        public void Info(string message) => File.AppendAllText(path, message + Environment.NewLine);
        public void Warning(string message) => Info("WARNING " + message);
        public void Error(string message, Exception? exception = null) => Info("ERROR " + message + " " + exception);
    }
}
