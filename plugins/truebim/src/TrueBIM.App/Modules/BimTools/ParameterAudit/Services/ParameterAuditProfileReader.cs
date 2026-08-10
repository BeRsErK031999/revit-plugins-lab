using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.Services;

public sealed class ParameterAuditProfileReader
{
    private static readonly string[] TemplateHeaders =
    [
        "RuleId",
        "Enabled",
        "Category",
        "Family",
        "Type",
        "ParameterName",
        "ParameterGuid",
        "BuiltInParameter",
        "Scope",
        "Required",
        "ExpectedValue",
        "AllowedValues",
        "Regex",
        "Min",
        "Max",
        "CaseSensitive",
        "Severity",
        "Message"
    ];

    public ParameterAuditProfile Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Путь к таблице правил не указан.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Таблица правил не найдена.", path);
        }

        TableData table = Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? ReadXlsx(path)
            : ReadDelimited(path);
        ValidateHeaders(table.Headers);

        List<ParameterAuditRule> rules = [];
        List<ParameterAuditProfileIssue> issues = [];
        foreach (TableRow row in table.Rows)
        {
            if (row.Values.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            ParameterAuditRule? rule = ParseRule(row, issues);
            if (rule is not null)
            {
                rules.Add(rule);
            }
        }

        foreach (IGrouping<string, ParameterAuditRule> duplicate in rules
                     .GroupBy(rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (ParameterAuditRule rule in duplicate)
            {
                issues.Add(Error(rule.LineNumber, rule.RuleId, $"RuleId «{rule.RuleId}» повторяется в таблице."));
            }
        }

        if (rules.Count == 0)
        {
            issues.Add(Error(0, string.Empty, "Таблица не содержит корректных правил."));
        }
        else if (!rules.Any(rule => rule.Enabled))
        {
            issues.Add(Error(0, string.Empty, "В таблице нет включённых правил."));
        }

        return new ParameterAuditProfile(rules, issues
            .OrderBy(issue => issue.LineNumber)
            .ThenBy(issue => issue.Message, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    public string CreateTemplate()
    {
        string[] requiredRoom =
        [
            "ROOM_DEPARTMENT", "true", "Помещения", "*", "*", "Назначение", "", "",
            "Instance", "true", "", "", "", "", "", "false", "Error",
            "У помещения должно быть заполнено назначение."
        ];
        string[] allowedDoorValue =
        [
            "DOOR_FIRE_RATING", "true", "Двери", "*", "*", "Огнестойкость", "", "",
            "Type", "true", "", "EI30|EI60|EI90", "", "", "", "false", "Error",
            "Выберите допустимый предел огнестойкости."
        ];
        string[] builtInMark =
        [
            "WALL_MARK", "true", "Стены", "*", "*", "Марка", "", "ALL_MODEL_MARK",
            "Instance", "true", "", "", "^[А-ЯA-Z0-9_-]+$", "", "", "false", "Warning",
            "Марка должна состоять из букв, цифр, дефиса или подчёркивания."
        ];

        return string.Join(Environment.NewLine,
        [
            JoinCsvRow(TemplateHeaders),
            JoinCsvRow(requiredRoom),
            JoinCsvRow(allowedDoorValue),
            JoinCsvRow(builtInMark)
        ]);
    }

    private static ParameterAuditRule? ParseRule(
        TableRow row,
        ICollection<ParameterAuditProfileIssue> issues)
    {
        string ruleId = Get(row, "RuleId");
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            ruleId = $"RULE_{row.LineNumber}";
        }

        bool isValid = true;
        if (!TryParseBool(Get(row, "Enabled"), true, out bool enabled))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Enabled должен быть true/false, да/нет или 1/0."));
            isValid = false;
        }

        string category = NormalizeSelector(Get(row, "Category"));
        if (string.IsNullOrWhiteSpace(category))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Не указана категория элементов."));
            isValid = false;
        }

        string parameterName = Get(row, "ParameterName").Trim();
        string guidText = Get(row, "ParameterGuid").Trim();
        Guid? parameterGuid = null;
        if (!string.IsNullOrWhiteSpace(guidText))
        {
            if (Guid.TryParse(guidText, out Guid parsedGuid))
            {
                parameterGuid = parsedGuid;
            }
            else
            {
                issues.Add(Error(row.LineNumber, ruleId, $"Некорректный ParameterGuid: {guidText}."));
                isValid = false;
            }
        }

        string builtInParameter = Get(row, "BuiltInParameter").Trim();
        if (parameterGuid.HasValue && !string.IsNullOrWhiteSpace(builtInParameter))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Нельзя одновременно указывать ParameterGuid и BuiltInParameter."));
            isValid = false;
        }

        if (!parameterGuid.HasValue
            && string.IsNullOrWhiteSpace(builtInParameter)
            && string.IsNullOrWhiteSpace(parameterName))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Укажите ParameterGuid, BuiltInParameter или ParameterName."));
            isValid = false;
        }

        if (!TryParseScope(Get(row, "Scope"), out ParameterAuditScope scope))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Scope должен быть Instance/Экземпляр или Type/Тип."));
            isValid = false;
        }

        if (!TryParseBool(Get(row, "Required"), true, out bool required))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Required должен быть true/false, да/нет или 1/0."));
            isValid = false;
        }

        if (!TryParseBool(Get(row, "CaseSensitive"), false, out bool caseSensitive))
        {
            issues.Add(Error(row.LineNumber, ruleId, "CaseSensitive должен быть true/false, да/нет или 1/0."));
            isValid = false;
        }

        if (!TryParseSeverity(Get(row, "Severity"), out ParameterAuditSeverity severity))
        {
            issues.Add(Error(row.LineNumber, ruleId, "Severity должен быть Error/Ошибка или Warning/Предупреждение."));
            isValid = false;
        }

        if (!TryParseNumber(Get(row, "Min"), out double? minimum))
        {
            issues.Add(Error(row.LineNumber, ruleId, $"Min не является числом: {Get(row, "Min")}."));
            isValid = false;
        }

        if (!TryParseNumber(Get(row, "Max"), out double? maximum))
        {
            issues.Add(Error(row.LineNumber, ruleId, $"Max не является числом: {Get(row, "Max")}."));
            isValid = false;
        }

        if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
        {
            issues.Add(Error(row.LineNumber, ruleId, "Min не может быть больше Max."));
            isValid = false;
        }

        string valuePattern = Get(row, "Regex").Trim();
        if (!string.IsNullOrWhiteSpace(valuePattern))
        {
            try
            {
                _ = new Regex(valuePattern);
            }
            catch (ArgumentException exception)
            {
                issues.Add(Error(row.LineNumber, ruleId, $"Некорректное регулярное выражение: {exception.Message}"));
                isValid = false;
            }
        }

        IReadOnlyList<string> allowedValues = Get(row, "AllowedValues")
            .Split(['|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!isValid)
        {
            return null;
        }

        ParameterAuditRule rule = new(
            row.LineNumber,
            ruleId.Trim(),
            enabled,
            category,
            NormalizeSelector(Get(row, "Family")),
            NormalizeSelector(Get(row, "Type")),
            parameterName,
            parameterGuid,
            builtInParameter,
            scope,
            required,
            Get(row, "ExpectedValue").Trim(),
            allowedValues,
            valuePattern,
            minimum,
            maximum,
            caseSensitive,
            severity,
            Get(row, "Message").Trim());

        if (!rule.Required && !rule.HasValueValidation)
        {
            issues.Add(new ParameterAuditProfileIssue(
                row.LineNumber,
                rule.RuleId,
                ParameterAuditProfileIssueSeverity.Warning,
                "Необязательное правило без ограничений не создаёт ошибок."));
        }

        if (!parameterGuid.HasValue
            && string.IsNullOrWhiteSpace(builtInParameter)
            && !string.IsNullOrWhiteSpace(parameterName))
        {
            issues.Add(new ParameterAuditProfileIssue(
                row.LineNumber,
                rule.RuleId,
                ParameterAuditProfileIssueSeverity.Warning,
                "Параметр ищется по имени. При совпадающих именах результат будет помечен как неоднозначный."));
        }

        return rule;
    }

    private static TableData ReadDelimited(string path)
    {
        string text = File.ReadAllText(path, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new TableData(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
        }

        char delimiter = DetectDelimiter(text);
        IReadOnlyList<IReadOnlyList<string>> records = ParseDelimited(text, delimiter);
        IReadOnlyList<string> headerRow = records.FirstOrDefault() ?? [];
        Dictionary<int, string> headers = BuildHeaders(headerRow);
        List<TableRow> rows = [];
        for (int index = 1; index < records.Count; index++)
        {
            rows.Add(new TableRow(index + 1, MapValues(headers, records[index])));
        }

        return new TableData(headers.Values.ToHashSet(StringComparer.OrdinalIgnoreCase), rows);
    }

    private static TableData ReadXlsx(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        ZipArchiveEntry sheetEntry = ResolveFirstWorksheet(archive)
            ?? throw new InvalidOperationException("XLSX не содержит листов.");
        using Stream stream = sheetEntry.Open();
        XDocument sheet = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        List<(int LineNumber, IReadOnlyList<string> Cells)> rows = [];
        foreach (XElement rowElement in sheet.Descendants(ns + "row"))
        {
            int lineNumber = int.TryParse((string?)rowElement.Attribute("r"), out int parsedRow)
                ? parsedRow
                : rows.Count + 1;
            Dictionary<int, string> cells = [];
            int fallbackColumn = 0;
            foreach (XElement cell in rowElement.Elements(ns + "c"))
            {
                int column = GetColumnIndex((string?)cell.Attribute("r")) ?? fallbackColumn;
                cells[column] = ReadXlsxCell(cell, sharedStrings, ns);
                fallbackColumn = column + 1;
            }

            int cellCount = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            string[] values = new string[cellCount];
            foreach (KeyValuePair<int, string> cell in cells)
            {
                values[cell.Key] = cell.Value;
            }

            rows.Add((lineNumber, values));
        }

        (int LineNumber, IReadOnlyList<string> Cells) header = rows
            .FirstOrDefault(row => row.Cells.Any(value => !string.IsNullOrWhiteSpace(value)));
        if (header.Cells is null)
        {
            return new TableData(new HashSet<string>(StringComparer.OrdinalIgnoreCase), []);
        }

        Dictionary<int, string> headers = BuildHeaders(header.Cells);
        List<TableRow> dataRows = rows
            .Where(row => row.LineNumber > header.LineNumber)
            .Select(row => new TableRow(row.LineNumber, MapValues(headers, row.Cells)))
            .ToList();
        return new TableData(headers.Values.ToHashSet(StringComparer.OrdinalIgnoreCase), dataRows);
    }

    private static ZipArchiveEntry? ResolveFirstWorksheet(ZipArchive archive)
    {
        ZipArchiveEntry? workbookEntry = archive.GetEntry("xl/workbook.xml");
        ZipArchiveEntry? relationsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (workbookEntry is not null && relationsEntry is not null)
        {
            XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace officeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
            using Stream workbookStream = workbookEntry.Open();
            XDocument workbook = XDocument.Load(workbookStream);
            string? relationId = (string?)workbook.Descendants(spreadsheet + "sheet")
                .FirstOrDefault()?
                .Attribute(officeRelationships + "id");
            if (!string.IsNullOrWhiteSpace(relationId))
            {
                using Stream relationsStream = relationsEntry.Open();
                XDocument relations = XDocument.Load(relationsStream);
                string? target = (string?)relations.Descendants(packageRelationships + "Relationship")
                    .FirstOrDefault(relationship => string.Equals(
                        (string?)relationship.Attribute("Id"),
                        relationId,
                        StringComparison.Ordinal))?
                    .Attribute("Target");
                if (!string.IsNullOrWhiteSpace(target))
                {
                    string normalizedTarget = target!.Replace('\\', '/').TrimStart('/');
                    string entryPath = normalizedTarget.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                        ? normalizedTarget
                        : $"xl/{normalizedTarget}";
                    ZipArchiveEntry? resolved = archive.GetEntry(entryPath);
                    if (resolved is not null)
                    {
                        return resolved;
                    }
                }
            }
        }

        return archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(entry =>
                entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return document.Descendants(ns + "si")
            .Select(item => string.Concat(item.Descendants(ns + "t").Select(text => text.Value)))
            .ToList();
    }

    private static string ReadXlsxCell(
        XElement cell,
        IReadOnlyList<string> sharedStrings,
        XNamespace ns)
    {
        string type = (string?)cell.Attribute("t") ?? string.Empty;
        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));
        }

        string value = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            && index >= 0
            && index < sharedStrings.Count)
        {
            return sharedStrings[index];
        }

        if (string.Equals(type, "b", StringComparison.OrdinalIgnoreCase))
        {
            return value == "1" ? "true" : "false";
        }

        return value;
    }

    private static int? GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return null;
        }

        int column = 0;
        int letterCount = 0;
        foreach (char character in cellReference!)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = (column * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
            letterCount++;
        }

        return letterCount == 0 ? null : column - 1;
    }

    private static Dictionary<int, string> BuildHeaders(IReadOnlyList<string> headerRow)
    {
        Dictionary<int, string> headers = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < headerRow.Count; index++)
        {
            string header = CanonicalHeader(headerRow[index]);
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (!seen.Add(header))
            {
                throw new InvalidOperationException($"Таблица содержит повторяющуюся колонку «{header}». ");
            }

            headers[index] = header;
        }

        return headers;
    }

    private static IReadOnlyDictionary<string, string> MapValues(
        IReadOnlyDictionary<int, string> headers,
        IReadOnlyList<string> cells)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<int, string> headerEntry in headers)
        {
            values[headerEntry.Value] = headerEntry.Key < cells.Count
                ? cells[headerEntry.Key] ?? string.Empty
                : string.Empty;
        }

        return values;
    }

    private static void ValidateHeaders(ISet<string> headers)
    {
        if (!headers.Contains("Category"))
        {
            throw new InvalidOperationException("Таблица не содержит обязательную колонку Category/Категория.");
        }

        if (!headers.Contains("ParameterName")
            && !headers.Contains("ParameterGuid")
            && !headers.Contains("BuiltInParameter"))
        {
            throw new InvalidOperationException(
                "Таблица должна содержать ParameterName, ParameterGuid или BuiltInParameter.");
        }
    }

    private static string Get(TableRow row, string header)
    {
        return row.Values.TryGetValue(header, out string? value) ? value : string.Empty;
    }

    private static string CanonicalHeader(string value)
    {
        string normalized = value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "ruleid" or "правило" or "кодправила" => "RuleId",
            "enabled" or "включено" or "активно" => "Enabled",
            "category" or "категория" => "Category",
            "family" or "семейство" => "Family",
            "type" or "тип" => "Type",
            "parameter" or "parametername" or "параметр" or "имяпараметра" => "ParameterName",
            "parameterguid" or "guid" => "ParameterGuid",
            "builtinparameter" or "builtin" or "встроенныйпараметр" => "BuiltInParameter",
            "scope" or "область" => "Scope",
            "required" or "обязательный" or "обязательно" => "Required",
            "expected" or "expectedvalue" or "ожидаемоезначение" => "ExpectedValue",
            "allowed" or "allowedvalues" or "допустимыезначения" => "AllowedValues",
            "regex" or "pattern" or "формат" => "Regex",
            "min" or "minimum" or "минимум" => "Min",
            "max" or "maximum" or "максимум" => "Max",
            "casesensitive" or "учитыватьрегистр" => "CaseSensitive",
            "severity" or "критичность" => "Severity",
            "message" or "сообщение" => "Message",
            _ => value.Trim()
        };
    }

    private static string NormalizeSelector(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "*" : value.Trim();
    }

    private static bool TryParseScope(string value, out ParameterAuditScope scope)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "Instance", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Экземпляр", StringComparison.CurrentCultureIgnoreCase))
        {
            scope = ParameterAuditScope.Instance;
            return true;
        }

        if (string.Equals(normalized, "Type", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Тип", StringComparison.CurrentCultureIgnoreCase))
        {
            scope = ParameterAuditScope.Type;
            return true;
        }

        scope = ParameterAuditScope.Instance;
        return false;
    }

    private static bool TryParseSeverity(string value, out ParameterAuditSeverity severity)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "Error", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Ошибка", StringComparison.CurrentCultureIgnoreCase))
        {
            severity = ParameterAuditSeverity.Error;
            return true;
        }

        if (string.Equals(normalized, "Warning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Предупреждение", StringComparison.CurrentCultureIgnoreCase))
        {
            severity = ParameterAuditSeverity.Warning;
            return true;
        }

        severity = ParameterAuditSeverity.Error;
        return false;
    }

    private static bool TryParseBool(string value, bool defaultValue, out bool result)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            result = defaultValue;
            return true;
        }

        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "да", StringComparison.CurrentCultureIgnoreCase)
            || normalized == "1")
        {
            result = true;
            return true;
        }

        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "нет", StringComparison.CurrentCultureIgnoreCase)
            || normalized == "0")
        {
            result = false;
            return true;
        }

        result = defaultValue;
        return false;
    }

    private static bool TryParseNumber(string value, out double? result)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            result = null;
            return true;
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant)
            || double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out invariant))
        {
            result = invariant;
            return true;
        }

        result = null;
        return false;
    }

    private static ParameterAuditProfileIssue Error(int lineNumber, string ruleId, string message)
    {
        return new ParameterAuditProfileIssue(
            lineNumber,
            ruleId,
            ParameterAuditProfileIssueSeverity.Error,
            message);
    }

    private static char DetectDelimiter(string text)
    {
        string firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? text;
        int semicolons = firstLine.Count(character => character == ';');
        int commas = firstLine.Count(character => character == ',');
        int tabs = firstLine.Count(character => character == '\t');
        if (tabs > semicolons && tabs > commas)
        {
            return '\t';
        }

        return semicolons >= commas ? ';' : ',';
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseDelimited(string text, char delimiter)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> row = [];
        StringBuilder cell = new();
        bool quoted = false;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else
                {
                    quoted = !quoted;
                }

                continue;
            }

            if (!quoted && character == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if (!quoted && (character == '\r' || character == '\n'))
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row.ToList());
                row.Clear();
                continue;
            }

            cell.Append(character);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static string JoinCsvRow(IEnumerable<string> values)
    {
        return string.Join(";", values.Select(value =>
            value.IndexOfAny([';', '"', '\r', '\n']) >= 0
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value));
    }

    private sealed record TableRow(
        int LineNumber,
        IReadOnlyDictionary<string, string> Values);

    private sealed record TableData(
        ISet<string> Headers,
        IReadOnlyList<TableRow> Rows);
}
