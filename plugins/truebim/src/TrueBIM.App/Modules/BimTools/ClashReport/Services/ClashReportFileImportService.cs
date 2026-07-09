using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashReportFileImportService
{
    private static readonly string[] HeaderMarkers =
    [
        "clashid",
        "id",
        "guid",
        "name",
        "clashname",
        "elementid1",
        "element1id",
        "elementid2",
        "element2id",
        "status",
        "comment"
    ];

    public ClashImportResult Import(string filePath)
    {
        Guard.NotNullOrWhiteSpace(filePath, nameof(filePath));

        string extension = Path.GetExtension(filePath);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ImportCsv(filePath);
        }

        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return ImportXml(filePath);
        }

        throw new NotSupportedException("Поддерживаются только файлы CSV и XML.");
    }

    private static ClashImportResult ImportCsv(string filePath)
    {
        string text = ReadText(filePath);
        char delimiter = DetectDelimiter(text);
        List<IReadOnlyList<string>> rows = ParseDelimited(text, delimiter)
            .Where(row => row.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList();
        List<ClashItem> items = [];
        List<string> messages = [];

        if (rows.Count == 0)
        {
            messages.Add("CSV-файл пустой.");
            return new ClashImportResult(items, messages);
        }

        bool hasHeader = HasHeader(rows[0]);
        Dictionary<string, int> headers = hasHeader
            ? BuildHeaderMap(rows[0])
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int firstDataRow = hasHeader ? 1 : 0;
        string source = Path.GetFileName(filePath);

        for (int index = firstDataRow; index < rows.Count; index++)
        {
            IReadOnlyList<string> row = rows[index];
            int rowNumber = index + 1;
            ClashItem item = hasHeader
                ? CreateCsvItemFromHeader(row, headers, source, rowNumber)
                : CreateCsvItemByPosition(row, source, rowNumber);
            items.Add(item);

            if (!item.ElementId1.HasValue && !item.ElementId2.HasValue && !item.HasPoint)
            {
                messages.Add($"Строка {rowNumber}: нет ElementId и координат для перехода.");
            }
        }

        messages.Insert(0, $"CSV импортирован: {items.Count} строк.");
        return new ClashImportResult(items, messages);
    }

    private static ClashImportResult ImportXml(string filePath)
    {
        XDocument document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        List<XElement> clashElements = document
            .Descendants()
            .Where(IsClashElement)
            .Where(HasClashData)
            .ToList();
        List<ClashItem> items = [];
        List<string> messages = [];
        string source = Path.GetFileName(filePath);

        if (document.Root is not null
            && IsClashElement(document.Root)
            && HasClashData(document.Root)
            && !clashElements.Contains(document.Root))
        {
            clashElements.Insert(0, document.Root);
        }

        for (int index = 0; index < clashElements.Count; index++)
        {
            ClashItem item = CreateXmlItem(clashElements[index], source, index + 1);
            items.Add(item);

            if (!item.ElementId1.HasValue && !item.ElementId2.HasValue && !item.HasPoint)
            {
                messages.Add($"XML-элемент {index + 1}: нет ElementId и координат для перехода.");
            }
        }

        if (items.Count == 0)
        {
            messages.Add("XML-файл не содержит распознанных clash/result/item записей.");
        }
        else
        {
            messages.Insert(0, $"XML импортирован: {items.Count} строк.");
        }

        return new ClashImportResult(items, messages);
    }

    private static ClashItem CreateCsvItemFromHeader(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        string defaultSource,
        int rowNumber)
    {
        string id = FirstValue(
            GetValue(row, headers, "clashid", "clash id", "id", "guid", "issueid"),
            $"Row-{rowNumber:0000}");
        string name = FirstValue(
            GetValue(row, headers, "clashname", "clash name", "name", "title", "description"),
            id);
        long? elementId1 = ParseLong(GetValue(row, headers, "elementid1", "element id 1", "element1id", "element 1 id", "id1", "elementa"));
        long? elementId2 = ParseLong(GetValue(row, headers, "elementid2", "element id 2", "element2id", "element 2 id", "id2", "elementb"));
        long? linkedElementId1 = ParseLong(GetValue(row, headers, "linkedelementid1", "linked element id 1", "linkedid1"));
        long? linkedElementId2 = ParseLong(GetValue(row, headers, "linkedelementid2", "linked element id 2", "linkedid2"));
        (double? x, double? y, double? z) = ReadPointFromCsv(row, headers);

        ClashItem item = new(
            id,
            name,
            elementId1,
            elementId2,
            x,
            y,
            z,
            ClashStatuses.Parse(GetValue(row, headers, "status", "state", "clashstatus", "reviewstatus")),
            GetValue(row, headers, "comment", "comments", "note", "notes"),
            GetValue(row, headers, "element1source", "source1", "model1", "link1"),
            GetValue(row, headers, "element2source", "source2", "model2", "link2"),
            linkedElementId2: linkedElementId2,
            linkedElementId1: linkedElementId1,
            source: FirstValue(GetValue(row, headers, "source", "file", "origin"), defaultSource));

        ApplyBounds(
            item,
            ParseDouble(GetValue(row, headers, "minx", "boundsminx")),
            ParseDouble(GetValue(row, headers, "miny", "boundsminy")),
            ParseDouble(GetValue(row, headers, "minz", "boundsminz")),
            ParseDouble(GetValue(row, headers, "maxx", "boundsmaxx")),
            ParseDouble(GetValue(row, headers, "maxy", "boundsmaxy")),
            ParseDouble(GetValue(row, headers, "maxz", "boundsmaxz")));

        return item;
    }

    private static ClashItem CreateCsvItemByPosition(IReadOnlyList<string> row, string source, int rowNumber)
    {
        string id = FirstValue(GetAt(row, 0), $"Row-{rowNumber:0000}");
        string name = FirstValue(GetAt(row, 1), id);
        return new ClashItem(
            id,
            name,
            ParseLong(GetAt(row, 2)),
            ParseLong(GetAt(row, 3)),
            ParseDouble(GetAt(row, 4)),
            ParseDouble(GetAt(row, 5)),
            ParseDouble(GetAt(row, 6)),
            ClashStatuses.Parse(GetAt(row, 7)),
            GetAt(row, 8),
            source: source);
    }

    private static ClashItem CreateXmlItem(XElement element, string defaultSource, int rowNumber)
    {
        List<long> elementIds = ExtractElementIds(element);
        string id = FirstValue(
            GetXmlValue(element, "clashid", "clash id", "guid", "id", "issueid"),
            $"XML-{rowNumber:0000}");
        string name = FirstValue(
            GetXmlValue(element, "clashname", "clash name", "name", "title", "description"),
            id);
        long? elementId1 = ParseLong(GetXmlValue(element, "elementid1", "element id 1", "element1id", "element 1 id", "id1"))
            ?? elementIds.ElementAtOrDefault(0).AsNullablePositive();
        long? elementId2 = ParseLong(GetXmlValue(element, "elementid2", "element id 2", "element2id", "element 2 id", "id2"))
            ?? elementIds.ElementAtOrDefault(1).AsNullablePositive();
        long? linkedElementId1 = ParseLong(GetXmlValue(element, "linkedelementid1", "linked element id 1", "linkedid1"));
        long? linkedElementId2 = ParseLong(GetXmlValue(element, "linkedelementid2", "linked element id 2", "linkedid2"));
        (double? x, double? y, double? z) = ReadPointFromXml(element);

        ClashItem item = new(
            id,
            name,
            elementId1,
            elementId2,
            x,
            y,
            z,
            ClashStatuses.Parse(GetXmlValue(element, "status", "state", "clashstatus", "reviewstatus")),
            GetXmlValue(element, "comment", "comments", "note", "notes"),
            GetXmlValue(element, "element1source", "source1", "model1", "link1"),
            GetXmlValue(element, "element2source", "source2", "model2", "link2"),
            linkedElementId2: linkedElementId2,
            linkedElementId1: linkedElementId1,
            source: FirstValue(GetXmlValue(element, "source", "file", "origin"), defaultSource));

        ApplyBounds(
            item,
            ParseDouble(GetXmlValue(element, "minx", "boundsminx")),
            ParseDouble(GetXmlValue(element, "miny", "boundsminy")),
            ParseDouble(GetXmlValue(element, "minz", "boundsminz")),
            ParseDouble(GetXmlValue(element, "maxx", "boundsmaxx")),
            ParseDouble(GetXmlValue(element, "maxy", "boundsmaxy")),
            ParseDouble(GetXmlValue(element, "maxz", "boundsmaxz")));

        return item;
    }

    private static string ReadText(string filePath)
    {
        try
        {
            return File.ReadAllText(filePath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
        }
        catch (DecoderFallbackException)
        {
            return File.ReadAllText(filePath, Encoding.Default);
        }
    }

    private static char DetectDelimiter(string text)
    {
        string firstLine = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        (char Delimiter, int Count)[] candidates =
        [
            (';', firstLine.Count(character => character == ';')),
            (',', firstLine.Count(character => character == ',')),
            ('\t', firstLine.Count(character => character == '\t'))
        ];

        return candidates.OrderByDescending(candidate => candidate.Count).First().Delimiter;
    }

    private static List<IReadOnlyList<string>> ParseDelimited(string text, char delimiter)
    {
        List<IReadOnlyList<string>> rows = [];
        List<string> currentRow = [];
        StringBuilder currentValue = new();
        bool inQuotes = false;

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    currentValue.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && character == delimiter)
            {
                currentRow.Add(currentValue.ToString().Trim());
                currentValue.Clear();
                continue;
            }

            if (!inQuotes && (character == '\r' || character == '\n'))
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                currentRow.Add(currentValue.ToString().Trim());
                currentValue.Clear();
                rows.Add(currentRow);
                currentRow = [];
                continue;
            }

            currentValue.Append(character);
        }

        if (currentValue.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentValue.ToString().Trim());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static bool HasHeader(IReadOnlyList<string> row)
    {
        return row
            .Select(NormalizeKey)
            .Any(normalized => HeaderMarkers.Contains(normalized, StringComparer.OrdinalIgnoreCase));
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> row)
    {
        Dictionary<string, int> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < row.Count; index++)
        {
            string key = NormalizeKey(row[index]);
            if (!string.IsNullOrWhiteSpace(key) && !headers.ContainsKey(key))
            {
                headers[key] = index;
            }
        }

        return headers;
    }

    private static string GetValue(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers,
        params string[] keys)
    {
        foreach (string key in keys.Select(NormalizeKey))
        {
            if (headers.TryGetValue(key, out int index))
            {
                return GetAt(row, index);
            }
        }

        return string.Empty;
    }

    private static string GetAt(IReadOnlyList<string> row, int index)
    {
        return index >= 0 && index < row.Count
            ? row[index].Trim()
            : string.Empty;
    }

    private static string GetXmlValue(XElement element, params string[] keys)
    {
        HashSet<string> normalizedKeys = new(keys.Select(NormalizeKey), StringComparer.OrdinalIgnoreCase);
        foreach (XAttribute attribute in element.Attributes())
        {
            if (normalizedKeys.Contains(NormalizeKey(attribute.Name.LocalName)))
            {
                return attribute.Value.Trim();
            }
        }

        foreach (XElement child in element.Elements())
        {
            if (!child.HasElements && normalizedKeys.Contains(NormalizeKey(child.Name.LocalName)))
            {
                return child.Value.Trim();
            }
        }

        foreach (XElement descendant in element.Descendants())
        {
            if (!descendant.HasElements && normalizedKeys.Contains(NormalizeKey(descendant.Name.LocalName)))
            {
                return descendant.Value.Trim();
            }
        }

        return string.Empty;
    }

    private static List<long> ExtractElementIds(XElement element)
    {
        List<long> ids = [];
        foreach (XElement descendant in element.Descendants())
        {
            string elementName = NormalizeKey(descendant.Name.LocalName);
            if (elementName.IndexOf("elementid", StringComparison.OrdinalIgnoreCase) >= 0
                && ParseLong(descendant.Value) is long directId)
            {
                ids.Add(directId);
            }
        }

        foreach (XElement attributeLike in element.Descendants().Where(IsAttributeLikeXmlElement))
        {
            string name = GetXmlValue(attributeLike, "name", "displayname", "attributename", "label");
            if (NormalizeKey(name).IndexOf("elementid", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            long? value = ParseLong(GetXmlValue(attributeLike, "value", "displayvalue", "text"));
            if (value.HasValue)
            {
                ids.Add(value.Value);
            }
        }

        return ids
            .Where(id => id > 0)
            .Distinct()
            .Take(2)
            .ToList();
    }

    private static (double? X, double? Y, double? Z) ReadPointFromCsv(
        IReadOnlyList<string> row,
        IReadOnlyDictionary<string, int> headers)
    {
        double? x = ParseDouble(GetValue(row, headers, "x", "pointx", "clashpointx"));
        double? y = ParseDouble(GetValue(row, headers, "y", "pointy", "clashpointy"));
        double? z = ParseDouble(GetValue(row, headers, "z", "pointz", "clashpointz"));
        if (x.HasValue && y.HasValue && z.HasValue)
        {
            return (x, y, z);
        }

        return ParseCombinedPoint(GetValue(row, headers, "point", "clashpoint", "location", "coordinates"));
    }

    private static (double? X, double? Y, double? Z) ReadPointFromXml(XElement element)
    {
        XElement? point = element
            .Descendants()
            .FirstOrDefault(descendant =>
                NormalizeKey(descendant.Name.LocalName) is "clashpoint" or "point" or "location" or "coordinates");

        double? x = ParseDouble(point?.Attribute("x")?.Value ?? GetXmlValue(element, "x", "pointx", "clashpointx"));
        double? y = ParseDouble(point?.Attribute("y")?.Value ?? GetXmlValue(element, "y", "pointy", "clashpointy"));
        double? z = ParseDouble(point?.Attribute("z")?.Value ?? GetXmlValue(element, "z", "pointz", "clashpointz"));
        if (x.HasValue && y.HasValue && z.HasValue)
        {
            return (x, y, z);
        }

        return ParseCombinedPoint(point?.Value ?? GetXmlValue(element, "point", "clashpoint", "location", "coordinates"));
    }

    private static (double? X, double? Y, double? Z) ParseCombinedPoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null, null);
        }

        string[] parts = value
            .Split([';', '|', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim())
            .ToArray();
        if (parts.Length < 3)
        {
            return (null, null, null);
        }

        return (ParseDouble(parts[0]), ParseDouble(parts[1]), ParseDouble(parts[2]));
    }

    private static bool IsClashElement(XElement element)
    {
        string name = NormalizeKey(element.Name.LocalName);
        return name is "clash" or "clashresult" or "result" or "item";
    }

    private static bool IsAttributeLikeXmlElement(XElement element)
    {
        string name = NormalizeKey(element.Name.LocalName);
        return name is "objectattribute" or "attribute" or "property";
    }

    private static bool HasClashData(XElement element)
    {
        return !string.IsNullOrWhiteSpace(GetXmlValue(element, "clashid", "guid", "id", "name", "clashname"))
            || ExtractElementIds(element).Count > 0
            || ReadPointFromXml(element) is { X: not null, Y: not null, Z: not null };
    }

    private static void ApplyBounds(
        ClashItem item,
        double? minX,
        double? minY,
        double? minZ,
        double? maxX,
        double? maxY,
        double? maxZ)
    {
        if (minX.HasValue && minY.HasValue && minZ.HasValue && maxX.HasValue && maxY.HasValue && maxZ.HasValue)
        {
            item.SetNavigationBounds(minX.Value, minY.Value, minZ.Value, maxX.Value, maxY.Value, maxZ.Value);
        }
    }

    private static long? ParseLong(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();
        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
        {
            return parsed;
        }

        Match match = Regex.Match(normalized, @"-?\d+");
        return match.Success && long.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
            ? parsed
            : null;
    }

    private static double? ParseDouble(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
    }

    private static string FirstValue(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}

file static class ClashImportLongExtensions
{
    public static long? AsNullablePositive(this long value)
    {
        return value > 0 ? value : null;
    }
}
