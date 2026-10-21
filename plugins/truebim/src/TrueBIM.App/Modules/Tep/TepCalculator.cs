using System.Text.RegularExpressions;

namespace TrueBIM.App.Modules.Tep;

public sealed class TepCalculator
{
    public TepCalculation Calculate(TepSnapshot snapshot, TepOptions options)
    {
        TepCalculation result = new();
        result.Issues.AddRange(snapshot.Issues);
        List<TepItem> included = snapshot.Items.Where(i => i.Included && i.Role != TepRoles.Ignore).ToList();
        string Identity(TepItem item) => string.IsNullOrEmpty(item.Key) ? item.Document + "/" + item.ElementId : item.Key;
        foreach (IGrouping<string, TepItem> duplicate in included.GroupBy(Identity).Where(g => g.Count() > 1))
            result.Issues.Add("Повтор источника исключён из сумм: " + duplicate.Key);
        included = included.GroupBy(Identity).Select(g => g.First()).ToList();
        foreach (TepItem item in included.Where(ZeroAreaRoom))
            result.Issues.Add("Пропущено помещение с нулевой площадью: " + item.Document + " / " + item.ElementId +
                             " «" + item.Name + "». Затронутые результаты отмечены как неполные.");
        foreach (TepItem item in included.Where(i => i.Problem.Length > 0 || i.Role == TepRoles.Unknown || !TepParts.All.Contains(i.Part)))
            result.Issues.Add(item.Document + " / " + item.ElementId + ": " +
                             (item.Problem.Length > 0 ? item.Problem : "Не определена категория или часть здания."));
        if (!options.ClassificationReviewed)
            result.Issues.Add("Предварительный расчёт. Проверьте классификацию помещений, зон, этажей и границ +4,500 во вкладках источников.");
        if (!options.PopulationBasisConfirmed)
            result.Issues.Add("Расчёт жильцов и сотрудников заблокирован: требуется нормативное основание удельных площадей для объекта в Бийске.");
        foreach (TepItem item in included.Where(i => !ZeroAreaRoom(i) && i.Role == TepRoles.Apartment && !i.RoomTypeKnown))
            result.Issues.Add(item.Document + " / " + item.ElementId + ": не определён тип помещения квартиры; проверьте жилую и холодную площадь.");

        foreach (TepMetric metric in TepMetrics.All)
        {
            TepResultRow row = new() { Metric = metric };
            foreach (string part in TepParts.All)
            {
                List<TepItem> partItems = included.Where(i => i.Part == part).ToList();
                TepValue value = included.Any(i => !ZeroAreaRoom(i) && !TepParts.All.Contains(i.Part))
                    ? TepValue.Missing("Есть включённые источники без части здания.")
                    : CalculatePartWithWarnings(metric.Row, part, partItems, snapshot.AvailableParts.Contains(part), options);
                if (value.Number.HasValue && !options.ClassificationReviewed)
                    value = value with { Status = value.IsPartial ? "Неполный расчёт" : "Предварительно",
                        Reason = Join(value.Reason, "Классификация источников не подтверждена.") };
                row.Values[part] = value;
            }
            TepValue[] values = TepParts.All.Select(p => row.Values[p]).ToArray();
            if (metric.Row is 11 or 12)
            {
                row.Values["Итог"] = values.Any(v => !v.Number.HasValue)
                    ? TepValue.Missing("Не определены этажи одной из частей.")
                    : new TepValue(null, string.Join("-", values.Select(v => v.Number!.Value.ToString("0"))),
                        values.Any(v => v.Status == "Предварительно") ? "Предварительно" : "Рассчитано",
                        "Порядок: Б1 — Стилобат — Б2; этажи не суммируются.", values.SelectMany(v => v.Sources).Distinct().ToArray());
            }
            else
            {
                row.Values["Итог"] = values.Any(v => !v.Number.HasValue)
                    ? TepValue.Missing("Неполные исходные данные: " + string.Join(", ", TepParts.All.Where(p => !row.Values[p].Number.HasValue)))
                    : TepValue.Numeric(values.Sum(v => v.Number!.Value), values.SelectMany(v => v.Sources).Distinct().ToArray()) with
                    {
                        IsPartial = values.Any(v => v.IsPartial),
                        Status = values.Any(v => v.IsPartial) ? "Неполный расчёт"
                            : values.Any(v => v.Status == "Предварительно") ? "Предварительно" : "Рассчитано",
                        Reason = string.Join("; ", TepParts.All.Where(p => row.Values[p].IsPartial)
                            .Select(p => p + ": " + row.Values[p].Reason))
                    };
                TepItem[] unassignedZero = included.Where(i => ZeroAreaRoom(i) && !TepParts.All.Contains(i.Part) && AffectsMetric(i, metric.Row)).ToArray();
                row.Values["Итог"] = WithSkippedRooms(row.Values["Итог"], unassignedZero);
            }
            result.Rows.Add(row);
        }
        return result;
    }

    private static string Join(string first, string second) => first.Length == 0 ? second : first + " " + second;

    private static bool ZeroAreaRoom(TepItem item) => item.Kind == "Помещение" && item.Area == 0;

    private static TepValue WithSkippedRooms(TepValue value, TepItem[] skipped)
    {
        if (skipped.Length == 0) return value;
        string warning = "Неполный расчёт: пропущены помещения с нулевой площадью: " +
                         string.Join(", ", skipped.Select(i => i.Document + " / " + i.ElementId + " «" + i.Name + "»")) + ".";
        return value with
        {
            IsPartial = true,
            Status = value.Number.HasValue ? "Неполный расчёт" : value.Status,
            Reason = Join(value.Reason, warning)
        };
    }

    private static TepValue CalculatePartWithWarnings(int row, string part, List<TepItem> items, bool available, TepOptions options)
    {
        TepItem[] skipped = items.Where(i => ZeroAreaRoom(i) && AffectsMetric(i, row)).ToArray();
        TepValue value = CalculatePart(row, part, items.Where(i => !ZeroAreaRoom(i)).ToList(), available, options);
        return WithSkippedRooms(value, skipped);
    }

    // Only warn for indicators to which the skipped room would contribute.
    // Unknown classification is conservatively relevant to every room indicator.
    private static bool AffectsMetric(TepItem item, int row)
    {
        if (row < 13 || row == 36) return false;
        if (item.Role == TepRoles.Unknown) return true;
        bool apartment = item.Role == TepRoles.Apartment;
        bool office = item.Role == TepRoles.Office;
        bool officeCommon = item.Role == TepRoles.OfficeCommon;
        return row switch
        {
            13 => apartment && (!item.RoomTypeKnown || item.Living && !item.Cold),
            14 or 37 => apartment && (!item.RoomTypeKnown || !item.Cold),
            15 or 16 or 17 => apartment,
            18 or 19 or 20 or 21 => apartment && (!item.RoomsInApartment.HasValue ||
                item.RoomsInApartment < 1 || item.RoomsInApartment > 4 || item.RoomsInApartment == row - 17),
            22 or 23 => item.Role == TepRoles.Storage,
            24 => office || officeCommon,
            25 or 26 => office && (!FloorNumber(item.Level).HasValue || FloorNumber(item.Level) == row - 24),
            27 => officeCommon,
            28 => (office || officeCommon) && item.CalculatedOffice,
            29 => (office || officeCommon) && item.UsefulOffice,
            30 => office,
            31 => item.Role == TepRoles.Common,
            32 => item.Role == TepRoles.Technical,
            33 => item.Role == TepRoles.Parking || item.Role == TepRoles.ParkingCommon,
            34 => item.Role == TepRoles.ParkingCommon,
            35 => item.Role == TepRoles.Parking,
            38 => (office || officeCommon) && item.EmployeeArea,
            _ => false
        };
    }

    private static TepValue CalculatePart(int row, string part, List<TepItem> items, bool roomDocumentAvailable, TepOptions options)
    {
        bool roomMetric = row >= 13;
        if (roomMetric && !roomDocumentAvailable)
            return TepValue.Missing("Не загружен документ с помещениями части " + part + ".");
        if (items.Any(i => i.Role == TepRoles.Unknown && (roomMetric ? i.Kind == "Помещение" : i.Kind == "Зона")))
            return TepValue.Missing("Есть неклассифицированные источники в этой части здания.");

        TepItem[] Select(params string[] roles) => items.Where(i => roles.Contains(i.Role)).ToArray();
        TepValue Sum(IEnumerable<TepItem> source, bool required = false, Func<TepItem, double>? multiplier = null)
        {
            TepItem[] selected = source.ToArray();
            if (required && selected.Length == 0) return TepValue.Missing("Не выбраны зоны для этого показателя.");
            TepItem[] invalid = selected.Where(i => !i.Area.HasValue || !Finite(i.Area.Value) || i.Area <= 0 || i.Problem.Length > 0).ToArray();
            if (invalid.Length > 0)
                return TepValue.Missing("Нет корректной площади или есть ошибка источника: " + string.Join(", ", invalid.Select(i => i.ElementId)));
            return TepValue.Numeric(selected.Sum(i => i.Area!.Value * (multiplier?.Invoke(i) ?? 1)), selected.Select(i => i.Key).ToArray());
        }
        TepValue Volume(string role)
        {
            TepItem[] selected = Select(role).Where(i => i.IncludeVolume).ToArray();
            if (selected.Length == 0 || selected.Any(i => !i.Height.HasValue || !Finite(i.Height.Value) || i.Height <= 0))
                return TepValue.Missing("Не заданы зоны и положительные расчётные высоты объёма.");
            return Sum(selected, true, i => i.Height!.Value) with { Reason = "Σ(площадь зоны × расчётная высота); границы зон должны соответствовать наружным поверхностям ограждений." };
        }
        TepValue Combine(params TepValue[] values) => values.Any(v => !v.Number.HasValue)
            ? TepValue.Missing(string.Join("; ", values.Where(v => !v.Number.HasValue).Select(v => v.Reason)))
            : TepValue.Numeric(values.Sum(v => v.Number!.Value), values.SelectMany(v => v.Sources).Distinct().ToArray());
        TepValue Count(IEnumerable<TepItem> source, bool groups)
        {
            TepItem[] selected = source.ToArray();
            if (selected.Any(i => i.Problem.Length > 0 || !i.Area.HasValue || i.Area <= 0) && selected.Any(i => i.Kind != "Машиноместо"))
                return TepValue.Missing("Есть неразмещённые, незамкнутые или избыточные помещения.");
            if (groups && selected.Any(i => string.IsNullOrWhiteSpace(i.Group)))
                return TepValue.Missing("Не заполнен идентификатор квартиры или коммерческого блока.");
            int count = groups ? selected.Select(i => i.Group.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() : selected.Length;
            return TepValue.Numeric(count, selected.Select(i => i.Key).ToArray());
        }

        TepItem[] apartments = Select(TepRoles.Apartment);
        if ((row is 13 or 14 or 15 or 37) && apartments.Any(i => !i.RoomTypeKnown))
            return TepValue.Missing("Не определён тип помещения квартиры. Проверьте жилые/холодные помещения и подтвердите тип.");
        switch (row)
        {
            case 2: return Sum(Select(TepRoles.Footprint), true);
            case 3: return Sum(Select(TepRoles.Footprint4500), true);
            case 4: return Combine(Sum(Select(TepRoles.BuildingAbove), true), Sum(Select(TepRoles.BuildingBelow), true), Sum(Select(TepRoles.Roof)));
            case 5: return Sum(Select(TepRoles.BuildingBelow), true);
            case 6: return Sum(Select(TepRoles.BuildingAbove), true);
            case 7: return Sum(Select(TepRoles.Roof));
            case 8: return Combine(Volume(TepRoles.BuildingBelow), Volume(TepRoles.BuildingAbove));
            case 9: return Volume(TepRoles.BuildingBelow);
            case 10: return Volume(TepRoles.BuildingAbove);
            case 11:
            case 12:
                TepItem[] floors = Select(TepRoles.BuildingAbove, TepRoles.BuildingBelow)
                    .Where(i => row == 11 ? i.CountAsStorey : i.CountAsFloor).ToArray();
                if (floors.Length == 0 || floors.Any(i => string.IsNullOrWhiteSpace(i.Level)))
                    return TepValue.Missing("Не определены расчётные этажи по зонам.");
                return TepValue.Numeric(floors.Select(i => i.Level).Distinct().Count(), floors.Select(i => i.Key).ToArray(),
                    "По уникальным уровням зон с флагами «Этажность» / «Количество этажей». Технические этажи и цоколь проверьте по СП 54.");
            case 13: return Sum(apartments.Where(i => i.Living && !i.Cold));
            case 14: return Sum(apartments.Where(i => !i.Cold));
            case 15:
                if (apartments.Any(i => i.Coefficient < 0 || i.Coefficient > 1 || double.IsNaN(i.Coefficient)))
                    return TepValue.Missing("Коэффициенты площадей должны лежать в диапазоне 0–1.");
                return Sum(apartments, false, i => i.Cold ? i.Coefficient : 1);
            case 16: return Sum(apartments);
            case 17: return Count(apartments, true);
            case 18:
            case 19:
            case 20:
            case 21:
                if (apartments.GroupBy(i => i.Group, StringComparer.OrdinalIgnoreCase).Any(g => g.Any(i => !i.RoomsInApartment.HasValue || i.RoomsInApartment < 1 || i.RoomsInApartment > 4) || g.Select(i => i.RoomsInApartment).Distinct().Count() != 1))
                    return TepValue.Missing("Не определена или противоречива комнатность квартиры.");
                return Count(apartments.Where(i => i.RoomsInApartment == row - 17), true);
            case 22: return Sum(Select(TepRoles.Storage));
            case 23: return Count(Select(TepRoles.Storage), false);
            case 24: return Sum(Select(TepRoles.Office, TepRoles.OfficeCommon));
            case 25: return Sum(Select(TepRoles.Office).Where(i => FloorNumber(i.Level) == 1));
            case 26: return Sum(Select(TepRoles.Office).Where(i => FloorNumber(i.Level) == 2));
            case 27: return Sum(Select(TepRoles.OfficeCommon));
            case 28: return Sum(Select(TepRoles.Office, TepRoles.OfficeCommon).Where(i => i.CalculatedOffice));
            case 29: return Sum(Select(TepRoles.Office, TepRoles.OfficeCommon).Where(i => i.UsefulOffice));
            case 30: return Count(Select(TepRoles.Office), true);
            case 31: return Sum(Select(TepRoles.Common));
            case 32: return Sum(Select(TepRoles.Technical));
            case 33: return Sum(Select(TepRoles.Parking, TepRoles.ParkingCommon));
            case 34: return Sum(Select(TepRoles.ParkingCommon));
            case 35: return Sum(Select(TepRoles.Parking));
            case 36:
                if (Select(TepRoles.ParkingPlace).Any(i => i.Problem.Length > 0))
                    return TepValue.Missing("Есть неоднозначные машиноместа. Проверьте номера и варианты.");
                return Count(Select(TepRoles.ParkingPlace), false);
            case 37:
            case 38:
                if (!options.PopulationBasisConfirmed || string.IsNullOrWhiteSpace(options.PopulationBasis))
                    return TepValue.Missing("Укажите и подтвердите нормативное основание для расчёта людей.");
                double divisor = row == 37 ? options.ResidentsAreaPerPerson : options.EmployeesAreaPerPerson;
                if (double.IsNaN(divisor) || double.IsInfinity(divisor) || divisor <= 0)
                    return TepValue.Missing("Удельная площадь должна быть положительным конечным числом.");
                TepValue basis = row == 37 ? Sum(apartments.Where(i => !i.Cold)) : Sum(Select(TepRoles.Office, TepRoles.OfficeCommon).Where(i => i.EmployeeArea));
                return !basis.Number.HasValue ? basis : TepValue.Numeric(basis.Number.Value / divisor, basis.Sources,
                    options.PopulationBasis + (row == 38 ? " База: помещения с флагом «Рабочие места»." : "") + " Расчётное значение сохранено без округления до целого.");
            default: return TepValue.Missing("Неизвестный показатель.");
        }
    }

    public static int? FloorNumber(string name)
    {
        Match match = Regex.Match(name, @"этаж\s*(-?\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out int floor) ? floor : null;
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
