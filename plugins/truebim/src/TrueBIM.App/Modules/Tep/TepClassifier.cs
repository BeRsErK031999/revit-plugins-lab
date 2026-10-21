using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.Tep;

/// <summary>Предварительная классификация по параметрам проекта D105; все решения доступны для проверки.</summary>
public static class TepClassifier
{
    public static void Room(TepItem item, string section, string groupType, int? roomType, double? coefficient, bool owningSection)
    {
        Match apartment = Regex.Match(groupType.Trim(), @"^([1-4])\s*[КK]$", RegexOptions.IgnoreCase);
        string text = (groupType + " " + item.Name).ToLowerInvariant();
        item.Included = owningSection;
        if (!owningSection) item.Review = "Другая секция или остаточная квартира в файле стилобата; исключено.";
        if (apartment.Success)
        {
            item.Role = TepRoles.Apartment;
            item.RoomsInApartment = int.Parse(apartment.Groups[1].Value);
            item.Living = roomType == 1;
            item.Cold = roomType is 3 or 4;
            item.Coefficient = coefficient ?? 1;
            if (roomType is null || roomType < 1 || roomType > 4)
            {
                item.RoomTypeKnown = false;
                item.Review = "Не задан корректный «• Помещение • Тип» (1–4). Назначьте «Жилое»/«Холодное» и отметьте «Тип проверен».";
            }
            if (item.Cold && !coefficient.HasValue)
                item.Problem = "Нет коэффициента холодного помещения.";
        }
        else if (text.Contains("кладов") && groupType.Contains("Клад"))
            item.Role = TepRoles.Storage;
        else if ((text.Contains("технич") && !text.Contains("коридор")) || text.Contains("сплит") || text.Contains("венткам") || text.Contains("венкам") || text.Contains("итп ") || text.Contains("эщ "))
            item.Role = TepRoles.Technical;
        else if (groupType.StartsWith("Офис", StringComparison.OrdinalIgnoreCase))
        {
            item.Role = Regex.IsMatch(groupType, @"\d") ? TepRoles.Office : TepRoles.OfficeCommon;
            item.CalculatedOffice = !Regex.IsMatch(item.Name, @"коридор|тамбур|^лк$|переход|шахт|лестниц|пандус|технич|венткам", RegexOptions.IgnoreCase);
            item.UsefulOffice = !Regex.IsMatch(item.Name, @"^лк$|шахт|лестниц|пандус", RegexOptions.IgnoreCase);
            item.EmployeeArea = Regex.IsMatch(item.Name, @"^офис$", RegexOptions.IgnoreCase);
            item.Review = "Расчётная/полезная площадь: проверьте назначение и исключения по приложению А СП 118.";
        }
        else if (section == "Паркинг" || groupType == "Парковка")
            item.Role = TepRoles.Parking;
        else if (section == "МОП нежилой части")
            item.Role = TepRoles.ParkingCommon;
        else if (groupType is "МОП" or "Квартиры" || Regex.IsMatch(item.Name, @"^ЛК$|тамбур", RegexOptions.IgnoreCase))
            item.Role = TepRoles.Common;
        else
            item.Role = TepRoles.Unknown;
    }

    public static void ResolveRoomCopies(TepSnapshot snapshot)
    {
        foreach (var duplicate in snapshot.Items.Where(i => i.Included && i.Kind == "Помещение" && i.Key.Contains("|"))
                     .GroupBy(i => i.Key.Substring(i.Key.LastIndexOf('|') + 1)).Where(g => g.Count() > 1))
        {
            TepItem[] towers = duplicate.Where(i => i.Part != TepParts.Stylobate).ToArray();
            if (towers.Length == 1)
            {
                foreach (TepItem copy in duplicate.Where(i => !ReferenceEquals(i, towers[0])))
                {
                    copy.Included = false;
                    copy.Review = "Копия помещения с тем же UniqueId. Использован источник " + towers[0].Document + ".";
                    snapshot.Issues.Add("Исключена копия помещения " + copy.ElementId + " из " + copy.Document + "; источник: " + towers[0].Document + ".");
                }
            }
            else
                foreach (TepItem item in duplicate) item.Problem = "Один UniqueId помещения в нескольких выбранных источниках; оставьте один.";
        }
    }

    public static void ParkingScope(TepItem item, bool? contained, string roomIds)
    {
        if (!item.Included) return;
        if (!contained.HasValue)
            item.Problem = "Нельзя определить расположение машиноместа: нет точки вставки или корректных помещений паркинга.";
        else if (!contained.Value)
        {
            item.Included = false;
            item.Review = "Вне помещений паркинга. Исключено по точке вставки; проверьте при необходимости.";
        }
        else
            item.Review = "Точка вставки внутри помещения паркинга, ID: " + roomIds + ".";
    }

    public static void Area(TepItem item, bool building, bool footprint, bool section4500)
    {
        string name = item.Name.ToLowerInvariant();
        item.Part = Regex.IsMatch(name, @"б\s*1|с\s*1") ? TepParts.B1
            : Regex.IsMatch(name, @"б\s*2|с\s*2") ? TepParts.B2 : TepParts.Stylobate;
        item.Included = building || footprint || section4500;
        item.CountAsStorey = item.Elevation >= -0.001;
        item.CountAsFloor = true;
        if (section4500) item.Role = TepRoles.Footprint4500;
        else if (footprint) item.Role = TepRoles.Footprint;
        else if (building)
        {
            item.Role = item.Elevation < -0.001 ? TepRoles.BuildingBelow : TepRoles.BuildingAbove;
            if (name.Contains("кровл") || name.Trim() == "стилобат")
            {
                item.Role = TepRoles.Roof;
                item.CountAsStorey = item.CountAsFloor = false;
                item.IncludeVolume = false;
                item.Review = "Подтвердите, что кровля эксплуатируемая, и её принадлежность части здания.";
            }
            if (name.Contains("террас"))
            {
                item.IncludeVolume = false;
                item.Review = "Открытая терраса: включена в площадь, исключена из объёма. Проверьте границы.";
            }
        }
        else
        {
            item.Role = TepRoles.Ignore;
            item.Review = "Не входит в выбранные спецификации зон ТЭП.";
        }
    }
}
