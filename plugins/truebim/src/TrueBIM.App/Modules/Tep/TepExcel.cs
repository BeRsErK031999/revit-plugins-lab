using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TrueBIM.App.Modules.Tep;

/// <summary>OOXML: оригинал сохраняется, расчёт и протокол добавляются отдельными листами.</summary>
public sealed class TepExcel
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] Columns = ["C", "D", "E", "F"];
    private static readonly string[] Parts = [TepParts.B1, TepParts.Stylobate, TepParts.B2, "Итог"];

    public void Export(string template, string destination, TepSnapshot snapshot, TepOptions options, TepCalculation result)
    {
        if (string.Equals(Path.GetFullPath(template), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Выберите новый файл: исходную таблицу заменять нельзя.");
        if (File.Exists(destination)) throw new IOException("Файл результата уже существует. Выберите новое имя.");
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(template, temporary);
            using (ZipArchive zip = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                XDocument workbook = Read(zip, "xl/workbook.xml");
                XDocument relations = Read(zip, "xl/_rels/workbook.xml.rels");
                XDocument types = Read(zip, "[Content_Types].xml");
                string[] shared = zip.GetEntry("xl/sharedStrings.xml") is null ? []
                    : Read(zip, "xl/sharedStrings.xml").Root!.Elements(S + "si").Select(e => string.Concat(e.Descendants(S + "t").Select(t => t.Value))).ToArray();
                XElement? originalSheet = null;
                XDocument? original = null;
                foreach (XElement sheet in workbook.Root!.Element(S + "sheets")!.Elements(S + "sheet"))
                {
                    string? id = (string?)sheet.Attribute(R + "id");
                    string? target = relations.Root!.Elements().FirstOrDefault(e => (string?)e.Attribute("Id") == id)?.Attribute("Target")?.Value;
                    if (target is null) continue;
                    string path = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                    XDocument candidate = Read(zip, path);
                    if (Enumerable.Range(0, 4).All(i => Text(Cell(candidate, Columns[i] + "1"), shared) == Parts[i]))
                    {
                        originalSheet = sheet;
                        original = candidate;
                        break;
                    }
                }
                if (original is null || originalSheet is null)
                    throw new InvalidDataException("Не найдена шапка C1:F1: Б1, Стилобат, Б2, Итог.");
                foreach (TepMetric metric in TepMetrics.All)
                    if (Normalize(Text(Cell(original, "B" + metric.Row), shared)) != Normalize(metric.Name))
                        throw new InvalidDataException("Строка " + metric.Row + " не соответствует шаблону: ожидается «" + metric.Name + "».");

                XDocument calculated = new(original);
                XElement? caption = calculated.Descendants(S + "c").FirstOrDefault(e => (string?)e.Attribute("r") == "B1");
                if (caption is null)
                {
                    caption = new XElement(S + "c", new XAttribute("r", "B1"));
                    calculated.Root!.Element(S + "sheetData")!.Elements(S + "row").First().AddFirst(caption);
                }
                bool hasPartial = result.Rows.Any(r => r.Values.Values.Any(v => v.IsPartial && v.Number.HasValue));
                WriteValue(caption, (options.ClassificationReviewed ? "РАСЧЁТ ПО МОДЕЛИ" : "ПРЕДВАРИТЕЛЬНЫЙ РАСЧЁТ") +
                    (hasPartial ? "\n* — неполный расчёт" : ""));
                if (hasPartial && caption.Parent is XElement headerRow)
                {
                    headerRow.SetAttributeValue("ht", Math.Max(64, (double?)headerRow.Attribute("ht") ?? 0));
                    headerRow.SetAttributeValue("customHeight", 1);
                }
                // Cloned worksheet has no relationships of its own; data and formatting remain.
                calculated.Root!.Elements().Where(e => e.Name.LocalName is "drawing" or "legacyDrawing" or "tableParts" or "hyperlinks" or "oleObjects").Remove();
                XDocument styles = Read(zip, "xl/styles.xml");
                Dictionary<string, string> formats = [];
                List<object?[]> links = [["Ячейка расчёта", "Ключ источника"]];
                List<object?[]> comparisons =
                [
                    ["Ячейка", "Показатель", "Часть", "Исходное значение (кэш Excel)", "Исходная формула",
                        "Расчёт", "Разница (расчёт − исходное)", "Статус", "Пояснение", "Ключи источников"]
                ];
                foreach (TepResultRow row in result.Rows)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        string address = Columns[i] + row.Metric.Row;
                        XElement oldCell = Cell(original, address);
                        string old = Text(oldCell, shared);
                        TepValue value = row.Values[Parts[i]];
                        XElement cell = Cell(calculated, address);
                        WriteValue(cell, value.Number.HasValue ? (object)value.Number.Value : value.Text);
                        cell.SetAttributeValue("s", NumberStyle(styles, (string?)cell.Attribute("s") ?? "0", row.Metric.Integer, formats,
                            value.IsPartial && value.Number.HasValue));
                        links.AddRange(value.Sources.Select(key => new object?[] { address, key }));
                        double? oldNumber = old.Trim() == "-" ? 0
                            : double.TryParse(old, NumberStyles.Float, Invariant, out double number) ? number : (double?)null;
                        bool equal = value.Number.HasValue && oldNumber.HasValue
                            ? Math.Round(value.Number.Value, row.Metric.Integer ? 0 : 1, MidpointRounding.AwayFromZero) ==
                              Math.Round(oldNumber.Value, row.Metric.Integer ? 0 : 1, MidpointRounding.AwayFromZero)
                            : value.Status != "Нет данных" && old == value.Text;
                        string status = value.Status == "Нет данных" ? "Нет данных"
                            : value.IsPartial ? "Неполный расчёт — " + (equal ? "число совпадает" : "расхождение")
                            : value.Status == "Предварительно" ? (equal ? "Совпадает предварительно" : "Расхождение предварительно")
                            : equal ? "Совпадает" : "Расхождение";
                        comparisons.Add([address, row.Metric.Name, Parts[i], old, oldCell.Element(S + "f")?.Value ?? "",
                            value.Number.HasValue ? (object)value.Number.Value : value.Text,
                            value.Number.HasValue && oldNumber.HasValue ? value.Number.Value - oldNumber.Value : null,
                            status, value.Reason, value.Sources.Length == 0 ? "" :
                                "Источников: " + value.Sources.Length + ". Полный перечень для " + address + " на листе «Связи расчёта»." ]);
                    }
                }
                string calcName = AddSheet(zip, workbook, relations, types, "Расчёт ТЭП", calculated);
                List<object?[]> diagnostics = [["Тип", "Описание"]];
                diagnostics.Add(["Расчёт", options.ClassificationReviewed ? "Классификация подтверждена пользователем." : "ПРЕДВАРИТЕЛЬНО. Классификация источников не подтверждена."]);
                diagnostics.Add(["Источник", Path.GetFullPath(template) + "; лист «" + originalSheet.Attribute("name")?.Value + "»"]);
                diagnostics.Add(["Модель", snapshot.Project + "; " + snapshot.CollectedAt.ToString("O")]);
                diagnostics.Add(["Метод", "Точность исходных площадей и высот сохранена. Отображение 0,1; сравнение после округления сумм, без округления слагаемых."]);
                diagnostics.Add(["Неполные результаты", "Жёлтые ячейки со знаком * рассчитаны по оставшимся помещениям: помещения с нулевой площадью пропущены, в том числе при подсчёте количества. Если пропущены все помещения показателя, результат 0 * означает неполный расчёт. Причины и ID — в «Сверке» и диагностике; числа в ячейках сохраняют полную точность."]);
                diagnostics.Add(["Нормы", "СП 54.13330.2022, приложение А; СП 118.13330.2022, приложение А. Геометрию границ, применимость исключений и действующие изменения проверяет проектировщик."]);
                diagnostics.Add(["Люди", options.PopulationBasis + "; подтверждено: " + options.PopulationBasisConfirmed]);
                diagnostics.Add(["Исходный Excel", "Сравнение с сохранёнными значениями (кэшем) формул. Пересчитайте исходник в Excel перед повторной проверкой; движок формул Excel не запускается."]);
                foreach (string issue in result.Issues) diagnostics.Add(["Диагностика", issue]);
                for (int row = 2; row <= 38; row++)
                {
                    if (row is 11 or 12) continue;
                    double? Num(string col)
                    {
                        string text = Text(Cell(original, col + row), shared).Trim();
                        return text is "-" or "" ? 0 : double.TryParse(text, NumberStyles.Float, Invariant, out double n) ? n : (double?)null;
                    }
                    double? total = Num("F"), sum = Num("C") + Num("D") + Num("E");
                    if (total.HasValue && sum.HasValue && Math.Abs(total.Value - sum.Value) > 0.0500001)
                        diagnostics.Add(["Арифметика исходника", "F" + row + " отличается от C" + row + "+D" + row + "+E" + row + " на " + (total.Value - sum.Value).ToString("0.###", Invariant) + "."]);
                }
                List<object?[]> sources =
                [
                    ["Ключ", "Документ", "ElementId", "Вид", "Учитывать", "Часть", "Категория", "Имя", "Номер", "Уровень", "Отметка, м",
                        "Площадь, м²", "Высота, м", "Группа", "Комнатность", "Жилое", "Холодное", "Коэффициент",
                        "Расчётная офисов", "Полезная офисов", "Объём", "Этажность", "Количество этажей", "Спецификации", "Ошибка", "Проверить", "Рабочие места", "Тип проверен"]
                ];
                sources.AddRange(snapshot.Items.Select(i => new object?[] { i.Key, i.Document, i.ElementId, i.Kind, i.Included, i.Part, i.Role,
                    i.Name, i.Number, i.Level, i.Elevation, i.Area, i.Height, i.Group, i.RoomsInApartment, i.Living, i.Cold, i.Coefficient,
                    i.CalculatedOffice, i.UsefulOffice, i.IncludeVolume, i.CountAsStorey, i.CountAsFloor, i.Schedules, i.Problem, i.Review, i.EmployeeArea, i.RoomTypeKnown }));
                string textStyle = TableStyle(styles, false);
                string headerStyle = TableStyle(styles, true);
                string decimalStyle = NumberStyle(styles, textStyle, false, formats);
                AddSheet(zip, workbook, relations, types, "Сверка", Table(comparisons,
                    [12, 55, 16, 22, 30, 18, 18, 30, 80, 60], textStyle, headerStyle, decimalStyle, [5, 6]));
                AddSheet(zip, workbook, relations, types, "Диагностика", Table(diagnostics,
                    [25, 130], textStyle, headerStyle, decimalStyle, []));
                AddSheet(zip, workbook, relations, types, "Источники", Table(sources,
                    [65, 55, 14, 18, 12, 16, 24, 28, 16, 30, 16, 18, 18, 25, 16, 12, 12, 16,
                        18, 18, 12, 16, 18, 65, 65, 80, 18, 18], textStyle, headerStyle, decimalStyle, []));
                AddSheet(zip, workbook, relations, types, "Связи расчёта", Table(links,
                    [20, 120], textStyle, headerStyle, decimalStyle, []));
                int active = workbook.Root!.Element(S + "sheets")!.Elements().ToList().FindIndex(e => (string?)e.Attribute("name") == calcName);
                workbook.Root.Element(S + "bookViews")?.Element(S + "workbookView")?.SetAttributeValue("activeTab", active);
                Write(zip, "xl/workbook.xml", workbook);
                Write(zip, "xl/_rels/workbook.xml.rels", relations);
                Write(zip, "[Content_Types].xml", types);
                Write(zip, "xl/styles.xml", styles);
            }
            File.Move(temporary, destination);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string NumberStyle(XDocument styles, string baseStyle, bool integer, Dictionary<string, string> cache, bool partial = false)
    {
        string key = baseStyle + ":" + integer + ":" + partial;
        if (cache.TryGetValue(key, out string? existing)) return existing;
        XElement xfs = styles.Root!.Element(S + "cellXfs")!;
        XElement? formats = styles.Root.Element(S + "numFmts");
        if (formats is null) { formats = new XElement(S + "numFmts"); styles.Root.AddFirst(formats); }
        int id = Math.Max(163, formats.Elements().Select(e => (int?)e.Attribute("numFmtId") ?? 0).DefaultIfEmpty(163).Max()) + 1;
        formats.Add(new XElement(S + "numFmt", new XAttribute("numFmtId", id),
            new XAttribute("formatCode", (integer ? "0" : "0.0") + (partial ? "\" *\"" : ""))));
        formats.SetAttributeValue("count", formats.Elements().Count());
        string index = xfs.Elements().Count().ToString(Invariant);
        XElement style = new(xfs.Elements().ElementAt(int.Parse(baseStyle, Invariant)));
        style.SetAttributeValue("numFmtId", id); style.SetAttributeValue("applyNumberFormat", 1);
        if (partial)
        {
            XElement fills = styles.Root.Element(S + "fills")!;
            int fill = fills.Elements().Count();
            fills.Add(new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "solid"),
                new XElement(S + "fgColor", new XAttribute("rgb", "FFFFE699")),
                new XElement(S + "bgColor", new XAttribute("indexed", 64)))));
            fills.SetAttributeValue("count", fills.Elements().Count());
            style.SetAttributeValue("fillId", fill);
            style.SetAttributeValue("applyFill", 1);
        }
        xfs.Add(style);
        xfs.SetAttributeValue("count", xfs.Elements().Count());
        cache[key] = index;
        return index;
    }

    private static string AddSheet(ZipArchive zip, XDocument workbook, XDocument rels, XDocument types, string name, XDocument sheet)
    {
        XElement sheets = workbook.Root!.Element(S + "sheets")!;
        string unique = name;
        int suffix = 2;
        while (sheets.Elements().Any(e => (string?)e.Attribute("name") == unique)) unique = name + " " + suffix++;
        int number = sheets.Elements().Select(e => (int?)e.Attribute("sheetId") ?? 0).DefaultIfEmpty().Max() + 1;
        string path = "xl/worksheets/tep" + Guid.NewGuid().ToString("N") + ".xml";
        string id = "rIdTep" + Guid.NewGuid().ToString("N");
        sheets.Add(new XElement(S + "sheet", new XAttribute("name", unique), new XAttribute("sheetId", number), new XAttribute(R + "id", id)));
        rels.Root!.Add(new XElement(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/worksheet"), new XAttribute("Target", path.Substring(3))));
        types.Root!.Add(new XElement(C + "Override", new XAttribute("PartName", "/" + path), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        Write(zip, path, sheet);
        return unique;
    }

    private static string TableStyle(XDocument styles, bool header)
    {
        XElement root = styles.Root!;
        XElement fonts = root.Element(S + "fonts")!;
        int font = fonts.Elements().Count();
        fonts.Add(new XElement(S + "font", new XElement(S + "sz", new XAttribute("val", 11)),
            new XElement(S + "name", new XAttribute("val", "Calibri")),
            header ? new XElement(S + "b") : null,
            new XElement(S + "color", new XAttribute("rgb", header ? "FFFFFFFF" : "FF172B4D"))));
        fonts.SetAttributeValue("count", fonts.Elements().Count());
        int fill = 0;
        if (header)
        {
            XElement fills = root.Element(S + "fills")!;
            fill = fills.Elements().Count();
            fills.Add(new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "solid"),
                new XElement(S + "fgColor", new XAttribute("rgb", "FF17365D")),
                new XElement(S + "bgColor", new XAttribute("indexed", 64)))));
            fills.SetAttributeValue("count", fills.Elements().Count());
        }
        XElement xfs = root.Element(S + "cellXfs")!;
        string index = xfs.Elements().Count().ToString(Invariant);
        xfs.Add(new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", font),
            new XAttribute("fillId", fill), new XAttribute("borderId", 0), new XAttribute("xfId", 0),
            new XAttribute("applyFont", 1), new XAttribute("applyFill", 1), new XAttribute("applyAlignment", 1),
            new XElement(S + "alignment", new XAttribute("vertical", "top"), new XAttribute("wrapText", 1))));
        xfs.SetAttributeValue("count", xfs.Elements().Count());
        return index;
    }

    private static XDocument Table(IReadOnlyList<object?[]> rows, double[] widths,
        string textStyle, string headerStyle, string decimalStyle, int[] decimalColumns)
    {
        XElement data = new(S + "sheetData");
        for (int r = 0; r < rows.Count; r++)
        {
            XElement row = new(S + "row", new XAttribute("r", r + 1));
            int lines = 1;
            for (int c = 0; c < rows[r].Length; c++)
            {
                string display = Convert.ToString(rows[r][c], Invariant) ?? "";
                int cellLines = display.Split('\n').Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / Math.Max(1, widths[c] - 3))));
                lines = Math.Max(lines, cellLines);
                XElement cell = new(S + "c", new XAttribute("r", Column(c + 1) + (r + 1)),
                    new XAttribute("s", r == 0 ? headerStyle : decimalColumns.Contains(c) ? decimalStyle : textStyle));
                WriteValue(cell, rows[r][c]);
                row.Add(cell);
            }
            row.SetAttributeValue("ht", Math.Min(409, Math.Max(r == 0 ? 34 : 30, lines * 16 + 8)));
            row.SetAttributeValue("customHeight", 1);
            data.Add(row);
        }
        return new XDocument(new XElement(S + "worksheet",
            new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0),
                new XElement(S + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"), new XAttribute("state", "frozen")))),
            new XElement(S + "cols", widths.Select((width, i) => new XElement(S + "col",
                new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", width), new XAttribute("customWidth", 1)))),
            data, new XElement(S + "autoFilter", new XAttribute("ref", "A1:" + Column(rows[0].Length) + rows.Count))));
    }

    private static string Column(int number) => number <= 26 ? ((char)('A' + number - 1)).ToString() : Column((number - 1) / 26) + (char)('A' + (number - 1) % 26);
    private static string Normalize(string value) => Regex.Replace(value.Trim().ToLowerInvariant().Replace('ё', 'е'), @"\s+", " ");
    private static XElement Cell(XDocument sheet, string address) => sheet.Descendants(S + "c").FirstOrDefault(e => (string?)e.Attribute("r") == address)
        ?? throw new InvalidDataException("В шаблоне отсутствует ячейка " + address + ".");
    private static string Text(XElement cell, string[] shared) => (string?)cell.Attribute("t") switch
    {
        "s" => shared[int.Parse(cell.Element(S + "v")!.Value, Invariant)],
        "inlineStr" => string.Concat(cell.Descendants(S + "t").Select(e => e.Value)),
        _ => cell.Element(S + "v")?.Value ?? ""
    };
    private static void WriteValue(XElement cell, object? value)
    {
        cell.RemoveNodes();
        cell.Attribute("t")?.Remove();
        if (value is null) return;
        if (value is double or float or int or long or decimal)
            cell.Add(new XElement(S + "v", Convert.ToString(value, Invariant)));
        else
        {
            cell.SetAttributeValue("t", "inlineStr");
            string text = value is bool flag ? (flag ? "Да" : "Нет") : value.ToString() ?? "";
            cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"),
                text.Length > 32767 ? text.Substring(0, 32720) + "\n… полный перечень см. лист Источники" : text)));
        }
    }
    private static XDocument Read(ZipArchive zip, string path)
    {
        using Stream stream = (zip.GetEntry(path) ?? throw new InvalidDataException("Нет части XLSX: " + path)).Open();
        return XDocument.Load(stream);
    }
    private static void Write(ZipArchive zip, string path, XDocument document)
    {
        zip.GetEntry(path)?.Delete();
        using Stream stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        document.Save(stream);
    }
}
