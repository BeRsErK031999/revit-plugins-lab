using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Tep;

public sealed class TepDocumentSource
{
    public Document Document { get; }
    public string Name => Document.Title;
    public TepDocumentSource(Document document) => Document = document;
}

public sealed class TepSourceSelection
{
    public TepDocumentSource? B1 { get; set; }
    public TepDocumentSource? B2 { get; set; }
    public TepDocumentSource? Stylobate { get; set; }
    public TepDocumentSource? Areas { get; set; }
    public string BuildingSchedule { get; set; } = "ТЭП • Строительный объем";
    public string FootprintSchedule { get; set; } = "ТЭП • Площадь застройки вся";
    public string SectionSchedule { get; set; } = "ТЭП • Площадь застройки отм. 4,500";
}

public sealed class TepCollector
{
    public static IReadOnlyList<TepDocumentSource> Documents(Document host)
    {
        List<TepDocumentSource> result = [];
        void Visit(Document document)
        {
            if (result.Any(s => s.Document.Equals(document))) return;
            result.Add(new TepDocumentSource(document));
            foreach (RevitLinkInstance link in new FilteredElementCollector(document).OfClass(typeof(RevitLinkInstance)))
                if (link.GetLinkDocument() is Document linked) Visit(linked);
        }
        Visit(host);
        return result;
    }

    public TepSnapshot Collect(Document host, TepSourceSelection selection)
    {
        TepSnapshot snapshot = new() { Project = host.Title };
        foreach (RevitLinkType link in new FilteredElementCollector(host).OfClass(typeof(RevitLinkType)))
            if (!RevitLinkType.IsLoaded(host, link.Id))
                snapshot.Issues.Add("Выгружена связь: " + link.Name + ". Проверьте, не формирует ли она границы помещений.");
        CollectRooms(snapshot, selection.B1, TepParts.B1, "1");
        CollectRooms(snapshot, selection.B2, TepParts.B2, "2");
        CollectRooms(snapshot, selection.Stylobate, TepParts.Stylobate, "");
        TepClassifier.ResolveRoomCopies(snapshot);
        if (selection.Areas is not null)
        {
            Document document = selection.Areas.Document;
            HashSet<long> building = Members(document, selection.BuildingSchedule, snapshot);
            HashSet<long> footprint = Members(document, selection.FootprintSchedule, snapshot);
            HashSet<long> section = Members(document, selection.SectionSchedule, snapshot);
            foreach (Area area in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().OfType<Area>())
            {
                TepItem item = Spatial(area, "Зона", "");
                item.Scheme = area.AreaScheme?.Name ?? "";
                item.Height = Number(area, "Зона Высота (TB)") is double feet ? feet * 0.3048 : null;
                TepClassifier.Area(item, building.Contains(item.ElementId), footprint.Contains(item.ElementId), section.Contains(item.ElementId));
                item.Schedules = string.Join("; ", new[]
                {
                    building.Contains(item.ElementId) ? selection.BuildingSchedule : "",
                    footprint.Contains(item.ElementId) ? selection.FootprintSchedule : "",
                    section.Contains(item.ElementId) ? selection.SectionSchedule : ""
                }.Where(s => s.Length > 0));
                if (new[] { building.Contains(item.ElementId), footprint.Contains(item.ElementId), section.Contains(item.ElementId) }.Count(x => x) > 1)
                    item.Problem = "Зона одновременно попала в разные виды расчёта. Выберите непересекающиеся спецификации.";
                snapshot.Items.Add(item);
            }
        }
        else snapshot.Issues.Add("Не выбран единый документ зон.");
        return snapshot;
    }

    private static HashSet<long> Members(Document document, string name, TepSnapshot snapshot)
    {
        ViewSchedule? view = new FilteredElementCollector(document).OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>().FirstOrDefault(s => !s.IsTemplate && s.Name == name);
        if (view is null)
        {
            snapshot.Issues.Add(document.Title + ": отсутствует спецификация «" + name + "».");
            return [];
        }
        return new HashSet<long>(new FilteredElementCollector(document, view.Id).OfCategory(BuiltInCategory.OST_Areas)
            .WhereElementIsNotElementType().ToElementIds().Select(RevitElementIds.GetValue));
    }

    private static void CollectRooms(TepSnapshot snapshot, TepDocumentSource? source, string part, string section)
    {
        if (source is null) return;
        Document document = source.Document;
        Room[] rooms = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType().OfType<Room>().ToArray();
        if (rooms.Length == 0)
        {
            snapshot.Issues.Add(document.Title + ": не найдено помещений для " + part + ".");
            return;
        }
        snapshot.AvailableParts.Add(part);
        ElementId latestPhase = document.Phases.get_Item(document.Phases.Size - 1).Id;
        foreach (Room room in rooms)
        {
            TepItem item = Spatial(room, "Помещение", part);
            string groupType = Text(room, "• Группа помещений • Тип");
            string roomSection = Text(room, "• Секция");
            item.Group = Text(room, "• Группа помещений • Номер");
            bool apartment = System.Text.RegularExpressions.Regex.IsMatch(groupType, @"^[1-4]\s*[КK]$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool owning = part == TepParts.Stylobate ? !apartment : roomSection == section;
            TepClassifier.Room(item, roomSection, groupType, (int?)Number(room, "• Помещение • Тип"),
                Number(room, "• Помещение • Коэффициент"), owning);
            if (room.get_Parameter(BuiltInParameter.ROOM_PHASE)?.AsElementId() is ElementId phase && phase != latestPhase)
            {
                item.Included = false;
                item.Review = "Не последняя стадия документа; исключено. При необходимости включите вручную.";
            }
            if (room.DesignOption is not null)
                item.Problem = "Помещение в варианте конструкции; явно выберите учитываемый вариант.";
            snapshot.Items.Add(item);
        }
        // Parking is owned by the selected stylobate document only.
        if (part != TepParts.Stylobate) return;
        HashSet<long> parkingRoomIds = new(snapshot.Items.Where(i => i.Document == document.Title && i.Included && i.Role == TepRoles.Parking && i.Area > 0)
            .Select(i => i.ElementId));
        Room[] parkingRooms = rooms.Where(r => parkingRoomIds.Contains(RevitElementIds.GetValue(r.Id))).ToArray();
        foreach (Element parking in new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Parking).WhereElementIsNotElementType())
        {
            TepItem item = new()
            {
                Key = Identity(parking), Document = document.Title, ElementId = RevitElementIds.GetValue(parking.Id),
                Name = parking.Name, Kind = "Машиноместо", Role = TepRoles.ParkingPlace, Part = part,
                Number = parking.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "",
                Level = (document.GetElement(parking.LevelId) as Level)?.Name ?? ""
            };
            if (parking.DesignOption is not null) item.Problem = "Машиноместо в варианте конструкции.";
            if (parking.GetPhaseStatus(latestPhase) is ElementOnPhaseStatus.Demolished or ElementOnPhaseStatus.Temporary or ElementOnPhaseStatus.None)
            {
                item.Included = false;
                item.Review = "Не существует в последней стадии документа.";
            }
            XYZ? point = (parking.Location as LocationPoint)?.Point;
            Room[] containing = point is null ? [] : parkingRooms
                .Where(r => r.IsPointInRoom(point) || r.IsPointInRoom(point + XYZ.BasisZ * 0.5)).ToArray();
            TepClassifier.ParkingScope(item, point is null || parkingRooms.Length == 0 ? (bool?)null : containing.Length > 0,
                string.Join(", ", containing.Select(r => RevitElementIds.GetValue(r.Id))));
            snapshot.Items.Add(item);
        }
        foreach (var duplicate in snapshot.Items.Where(i => i.Role == TepRoles.ParkingPlace && i.Included && i.Number.Length > 0)
                     .GroupBy(i => i.Number).Where(g => g.Count() > 1))
            foreach (TepItem item in duplicate) item.Problem = "Повтор номера машиноместа: " + duplicate.Key;
    }

    private static TepItem Spatial(SpatialElement element, string kind, string part)
    {
        Level? level = element.Document.GetElement(element.LevelId) as Level;
        double area = element is Room room ? room.Area : ((Area)element).Area;
        return new TepItem
        {
            Key = Identity(element), Document = element.Document.Title, ElementId = RevitElementIds.GetValue(element.Id),
            Kind = kind, Part = part, Name = element.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? element.Name,
            Number = element.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
            Area = area * 0.09290304, Level = level?.Name ?? "", Elevation = (level?.Elevation ?? 0) * 0.3048,
            Problem = area <= 0 ? "Нет положительной площади: неразмещённый, незамкнутый или избыточный источник." : ""
        };
    }

    private static string Identity(Element element) => (string.IsNullOrEmpty(element.Document.PathName)
        ? element.Document.Title : element.Document.PathName) + "|" + element.UniqueId;
    private static string Text(Element element, string name) => element.LookupParameter(name)?.AsString() ?? "";
    private static double? Number(Element element, string name)
    {
        Parameter? parameter = element.LookupParameter(name);
        if (parameter is null || !parameter.HasValue) return null;
        return parameter.StorageType == StorageType.Double ? parameter.AsDouble()
            : parameter.StorageType == StorageType.Integer ? parameter.AsInteger() : (double?)null;
    }
}
