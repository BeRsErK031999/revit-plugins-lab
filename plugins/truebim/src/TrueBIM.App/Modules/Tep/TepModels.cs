using System.Globalization;

namespace TrueBIM.App.Modules.Tep;

public static class TepParts
{
    public const string B1 = "Б1";
    public const string Stylobate = "Стилобат";
    public const string B2 = "Б2";
    public static string[] All => [B1, Stylobate, B2];
}

public static class TepRoles
{
    public const string Ignore = "Не учитывать";
    public const string Apartment = "Квартира";
    public const string Storage = "Кладовая";
    public const string Office = "Коммерческие помещения";
    public const string OfficeCommon = "МОП коммерции";
    public const string Common = "МОП";
    public const string Technical = "Технические помещения";
    public const string Parking = "Паркинг — проезды и хранение";
    public const string ParkingCommon = "МОП паркинга";
    public const string ParkingPlace = "Машиноместо";
    public const string BuildingAbove = "Площадь здания выше 0";
    public const string BuildingBelow = "Площадь здания ниже 0";
    public const string Roof = "Эксплуатируемая кровля";
    public const string Footprint = "Площадь застройки";
    public const string Footprint4500 = "Сечение +4,500";
    public const string Unknown = "Требуется классификация";
    public static string[] All =>
    [
        Unknown, Ignore, Apartment, Storage, Office, OfficeCommon, Common, Technical,
        Parking, ParkingCommon, ParkingPlace, BuildingAbove, BuildingBelow, Roof, Footprint, Footprint4500
    ];
}

/// <summary>Снимок источника. Площади в м², высоты в м; округление при чтении запрещено.</summary>
public sealed class TepItem
{
    public string Key { get; set; } = "";
    public string Document { get; set; } = "";
    public long ElementId { get; set; }
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public string Level { get; set; } = "";
    public double Elevation { get; set; }
    public string Scheme { get; set; } = "";
    public string Part { get; set; } = "";
    public string Role { get; set; } = TepRoles.Unknown;
    public bool Included { get; set; } = true;
    public double? Area { get; set; }
    public double? Height { get; set; }
    public string Group { get; set; } = "";
    public int? RoomsInApartment { get; set; }
    public bool Living { get; set; }
    public bool Cold { get; set; }
    public double Coefficient { get; set; } = 1;
    public bool UsefulOffice { get; set; } = true;
    public bool CalculatedOffice { get; set; } = true;
    public bool EmployeeArea { get; set; }
    public bool RoomTypeKnown { get; set; } = true;
    public bool IncludeVolume { get; set; } = true;
    public bool CountAsStorey { get; set; } = true;
    public bool CountAsFloor { get; set; } = true;
    public string Review { get; set; } = "";
    public string Schedules { get; set; } = "";
    public string Problem { get; set; } = "";
}

public sealed class TepSnapshot
{
    public string Project { get; set; } = "";
    public List<TepItem> Items { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public List<string> AvailableParts { get; set; } = [];
    public DateTime CollectedAt { get; set; } = DateTime.Now;
}

public sealed class TepOptions
{
    public double ResidentsAreaPerPerson { get; set; } = 40;
    public double EmployeesAreaPerPerson { get; set; } = 6;
    // These values originate in the supplied workbook, not from an inferred legal classification.
    public string PopulationBasis { get; set; } = "Исходный Excel: площадь квартир / 40; коммерческие помещения без МОП / 6. Нормативное основание требует подтверждения.";
    public bool PopulationBasisConfirmed { get; set; }
    public bool ClassificationReviewed { get; set; }
}

public sealed record TepMetric(int Row, string Name, string Unit, bool Integer = false);

public static class TepMetrics
{
    public static IReadOnlyList<TepMetric> All { get; } =
    [
        new(2, "Площадь застройки", "м²"),
        new(3, "Площадь застройки на отм. 4,500", "м²"),
        new(4, "Площадь здания", "м²"),
        new(5, "в том числе ниже 0.000", "м²"),
        new(6, "в том числе выше 0.000", "м²"),
        new(7, "в том числе эксплуатируемая кровля", "м²"),
        new(8, "Строительный объем", "м³"),
        new(9, "в том числе ниже 0.000", "м³"),
        new(10, "в том числе выше 0.000", "м³"),
        new(11, "Этажность", "этаж", true),
        new(12, "Количество этажей", "этаж", true),
        new(13, "Жилая площадь квартир", "м²"),
        new(14, "Площадь квартир без учета лоджии", "м²"),
        new(15, "Общая площадь квартир с учетом неотапливаемых помещений с коэффициентом", "м²"),
        new(16, "Общая площадь квартир с учетом неотапливаемых помещений без коэффициента", "м²"),
        new(17, "Количество квартир, в том числе:", "шт.", true),
        new(18, "1-комнатные", "шт.", true),
        new(19, "2-комнатные", "шт.", true),
        new(20, "3-комнатные", "шт.", true),
        new(21, "4-комнатные", "шт.", true),
        new(22, "Площадь кладовых", "м²"),
        new(23, "Количество кладовых", "шт.", true),
        new(24, "Общая площадь встроенных коммерческих помещений", "м²"),
        new(25, "в том числе 1 этаж", "м²"),
        new(26, "в том числе 2 этаж", "м²"),
        new(27, "в том числе МОП", "м²"),
        new(28, "расчетная площадь офисов", "м²"),
        new(29, "полезная площадь офисов", "м²"),
        new(30, "Количество встроенных коммерческих помещений", "шт.", true),
        new(31, "Площадь помещений МОП", "м²"),
        new(32, "Площадь технических помещений", "м²"),
        new(33, "Площадь помещений паркинга", "м²"),
        new(34, "в том числе МОП нежилой части", "м²"),
        new(35, "в том числе площадь проездов и мест хранения автомобилей", "м²"),
        new(36, "Кол-во машиномест", "шт.", true),
        new(37, "Расчетное кол-во жильцов", "чел."),
        new(38, "Расчетное кол-во сотрудников", "чел.")
    ];
}

public sealed record TepValue(double? Number, string Text, string Status, string Reason, string[] Sources)
{
    public bool IsPartial { get; init; }
    public static TepValue Missing(string reason) => new(null, "Нет данных", "Нет данных", reason, []);
    public static TepValue Numeric(double value, string[] sources, string reason = "") =>
        new(value, value.ToString("0.0", CultureInfo.GetCultureInfo("ru-RU")), "Рассчитано", reason, sources);
}

public sealed class TepResultRow
{
    public TepMetric Metric { get; set; } = null!;
    public Dictionary<string, TepValue> Values { get; set; } = [];
    public string Name => Metric.Name;
    public string Unit => Metric.Unit;
    public string B1 => Display(TepParts.B1);
    public string Stylobate => Display(TepParts.Stylobate);
    public string B2 => Display(TepParts.B2);
    public string Total => Display("Итог");
    public string Notes => string.Join("; ", Values.Where(p => p.Value.Reason.Length > 0).GroupBy(p => p.Value.Reason)
        .Select(g => string.Join(", ", g.Select(p => p.Key)) + ": " + g.Key));
    private string Display(string part) => Values.TryGetValue(part, out TepValue? value)
        ? (value.Number?.ToString(Metric.Integer ? "0" : "0.0", CultureInfo.GetCultureInfo("ru-RU")) ?? value.Text) +
          (value.IsPartial && value.Number.HasValue ? " *" : "")
        : "Нет данных";
}

public sealed class TepCalculation
{
    public List<TepResultRow> Rows { get; set; } = [];
    public List<string> Issues { get; set; } = [];
}
