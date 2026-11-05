using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace TrueBIM.App.Modules.GeometricFinishes;

public sealed class FinishCatalog
{
    public Document Document { get; }
    public View View { get; }
    private readonly HashSet<ElementId> selected;
    public IReadOnlyList<Room> Rooms { get; }
    public IReadOnlyList<Level> Levels { get; }
    public FinishCatalog(UIDocument ui)
    {
        Document = ui.Document;
        View = ui.ActiveView;
        selected = new HashSet<ElementId>(ui.Selection.GetElementIds());
        Rooms = new FilteredElementCollector(Document).OfClass(typeof(SpatialElement)).OfType<Room>().ToArray();
        Levels = new FilteredElementCollector(Document).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToArray();
    }
    public bool HasSelectedRooms => Rooms.Any(r => selected.Contains(r.Id));
    public static string RoomName(Room room) => room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name;
    public static string RoomLabel(Room room) => room.Number + " · " + RoomName(room);
    public static BuiltInCategory CategoryFor(FinishSettings settings) => settings.Kind == FinishKind.Walls ? BuiltInCategory.OST_Walls
        : settings.Kind == FinishKind.Ceilings && !settings.CeilingAsFloor ? BuiltInCategory.OST_Ceilings : BuiltInCategory.OST_Floors;

    public IReadOnlyList<FinishTypeOption> Types(FinishSettings settings)
    {
        return new FilteredElementCollector(Document).OfCategory(CategoryFor(settings)).WhereElementIsElementType()
            .OfType<HostObjAttributes>().Where(t => t is not WallType w || w.Kind == WallKind.Basic)
            .Select(t => new FinishTypeOption { Id = t.Id, UniqueId = t.UniqueId, Name = t.Name,
                ThicknessMm = (t.GetCompoundStructure()?.GetWidth() ?? 0) * 304.8,
                Parameters = t.Parameters.Cast<Parameter>().GroupBy(FinishParameters.Key).ToDictionary(g => g.Key, g => FinishParameters.Text(g.First())) })
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public IReadOnlyList<Room> SelectRooms(FinishSettings settings)
    {
        IEnumerable<Room> rooms = Rooms;
        if (settings.Scope == FinishScope.Selected) rooms = rooms.Where(r => selected.Contains(r.Id));
        else if (settings.Scope == FinishScope.Level) rooms = rooms.Where(r => Document.GetElement(r.LevelId)?.UniqueId == settings.LevelUniqueId);
        else if (settings.Scope == FinishScope.ActiveView)
        {
            if (!FilteredElementCollector.IsViewValidForElementIteration(Document, View.Id))
                throw new InvalidOperationException("Активный вид не поддерживает выбор помещений. Откройте план или выберите другой режим.");
            var visible = new HashSet<ElementId>(new FilteredElementCollector(Document, View.Id).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().ToElementIds());
            rooms = rooms.Where(r => visible.Contains(r.Id));
        }
        if (!string.IsNullOrWhiteSpace(settings.RoomFilter))
        {
            string filter = settings.RoomFilter.Trim();
            rooms = rooms.Where(r => (RoomLabel(r) + " " + FinishParameters.Text(r.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT)))
                .IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }
        return rooms.OrderBy(r => r.Number, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public FinishPlan BuildPlan(FinishSettings settings)
    {
        string? validation = settings.Validate();
        if (validation != null) throw new InvalidOperationException(validation);
        FinishPlan plan = new();
        IReadOnlyList<Room> rooms = SelectRooms(settings);
        plan.RoomCount = rooms.Count;
        foreach (Room room in rooms)
        {
            try
            {
                if (room.Location == null || room.Area <= 1e-8) throw new InvalidOperationException("Помещение не размещено, не замкнуто или имеет нулевую площадь.");
                using SpatialElementBoundaryOptions options = new() { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
                IList<IList<BoundarySegment>> boundaries = room.GetBoundarySegments(options);
                if (boundaries == null || boundaries.Count == 0) throw new InvalidOperationException("Не найден замкнутый контур помещения.");
                if (settings.Kind != FinishKind.Walls)
                {
                    IList<CurveLoop> loops = boundaries.Select(b => FinishGeometry.ClosedRoomLoop(b, message => plan.Issues.Add(Issue(room, message)))).ToList();
                    plan.Targets.Add(new FinishTarget { Room = room, Loops = loops, Label = RoomName(room),
                        Key = FinishSettings.AssignmentKey(FinishGrouping.RoomName, RoomName(room), "") });
                    continue;
                }
                int before = plan.Targets.Count;
                foreach (BoundarySegment boundary in boundaries.SelectMany(b => b))
                {
                    Element? element = Document.GetElement(boundary.ElementId);
                    RevitLinkInstance? link = element as RevitLinkInstance;
                    Wall? wall = link == null ? element as Wall : link.GetLinkDocument()?.GetElement(boundary.LinkElementId) as Wall;
                    if (wall == null) continue; // Room separation lines and columns are not wall hosts.
                    if (FinishOwnership.IsManaged(wall))
                    {
                        plan.Issues.Add(Issue(room, "Часть границы образована ранее созданной отделкой TrueBIM; повторный слой на этой границе пропущен."));
                        continue;
                    }
                    if (wall.WallType.Kind == WallKind.Curtain) continue;
                    string typeKey = (link == null ? "host:" : "link:" + link.GetTypeId() + ":") + wall.WallType.UniqueId;
                    string typeName = wall.WallType.Name + (link == null ? "" : " [связь]");
                    string label = settings.Grouping == FinishGrouping.WallType ? typeName : settings.Grouping == FinishGrouping.RoomName ? RoomName(room) : RoomName(room) + " / " + typeName;
                    plan.Targets.Add(new FinishTarget { Room = room, Boundary = boundary.GetCurve(), Label = label,
                        Key = FinishSettings.AssignmentKey(settings.Grouping, RoomName(room), typeKey),
                        Source = new FinishSourceWall { Wall = wall, Linked = link != null, InstanceKey = link?.UniqueId ?? "host", Transform = link?.GetTotalTransform() ?? Transform.Identity, TypeKey = typeKey, TypeName = typeName } });
                }
                if (plan.Targets.Count == before) plan.Issues.Add(Issue(room, "Нет подходящих стен по границе помещения. Проверьте границы и загрузку связей."));
            }
            catch (Exception ex) when (ex is not Autodesk.Revit.Exceptions.OperationCanceledException)
            { plan.Issues.Add(Issue(room, ex.Message)); }
        }
        return plan;
    }
    public static FinishIssue Issue(Room room, string message) => new() { RoomId = room.Id, Room = RoomLabel(room), Message = message };
}
