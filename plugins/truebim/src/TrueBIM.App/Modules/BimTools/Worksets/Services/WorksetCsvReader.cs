using TrueBIM.App.Modules.BimTools.Worksets.Models;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace TrueBIM.App.Modules.BimTools.Worksets.Services;

public sealed class WorksetCsvReader
{
    private static readonly string[] TemplateRows =
    [
        "WorksetName",
        "АР_Стены",
        "АР_Двери",
        "АР_Окна",
        "КР_Несущие конструкции",
        "ОВ_Воздуховоды",
        "ВК_Трубы",
        "ЭОМ_Оборудование",
        "Связи_RVT",
        "Координация"
    ];

    public IReadOnlyList<WorksetImportRow> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Import path is empty.", nameof(path));
        }

        if (Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return ReadXlsx(path);
        }

        string[] lines = File.ReadAllLines(path);
        List<WorksetImportRow> rows = new();
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            string value = ReadFirstCell(line);
            if (index == 0 && IsHeader(value))
            {
                continue;
            }

            rows.Add(new WorksetImportRow(index + 1, value, NormalizeWorksetName(value)));
        }

        return rows;
    }

    public void WriteTemplate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Template path is empty.", nameof(path));
        }

        if (Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            WriteXlsxTemplate(path);
            return;
        }

        File.WriteAllLines(path, TemplateRows, Encoding.UTF8);
    }

    public static string NormalizeWorksetName(string value)
    {
        string trimmed = value.Trim();
        while (trimmed.IndexOf("  ", StringComparison.Ordinal) >= 0)
        {
            trimmed = trimmed.Replace("  ", " ");
        }

        return trimmed;
    }

    private static bool IsHeader(string value)
    {
        return string.Equals(value.Trim(), "WorksetName", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "Workset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value.Trim(), "Рабочий набор", StringComparison.CurrentCultureIgnoreCase);
    }

    private static IReadOnlyList<WorksetImportRow> ReadXlsx(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        Dictionary<int, string> sharedStrings = ReadSharedStrings(archive);
        ZipArchiveEntry sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("XLSX не содержит листов.");

        using Stream stream = sheetEntry.Open();
        XDocument sheet = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        List<WorksetImportRow> rows = [];
        foreach (XElement rowElement in sheet.Descendants(ns + "row"))
        {
            int rowNumber = TryParseRowNumber((string?)rowElement.Attribute("r")) ?? rows.Count + 1;
            XElement? firstCell = rowElement
                .Elements(ns + "c")
                .FirstOrDefault(cell => IsColumnA((string?)cell.Attribute("r")))
                ?? rowElement.Elements(ns + "c").FirstOrDefault();
            string value = ReadCellValue(firstCell, sharedStrings, ns);
            if (rowNumber == 1 && IsHeader(value))
            {
                continue;
            }

            rows.Add(new WorksetImportRow(rowNumber, value, NormalizeWorksetName(value)));
        }

        return rows;
    }

    private static Dictionary<int, string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Dictionary<int, string> sharedStrings = [];
        int index = 0;
        foreach (XElement item in document.Descendants(ns + "si"))
        {
            sharedStrings[index] = string.Concat(item.Descendants(ns + "t").Select(text => text.Value));
            index++;
        }

        return sharedStrings;
    }

    private static string ReadCellValue(XElement? cell, IReadOnlyDictionary<int, string> sharedStrings, XNamespace ns)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        string type = (string?)cell.Attribute("t") ?? string.Empty;
        if (string.Equals(type, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(ns + "t").Select(text => text.Value));
        }

        string rawValue = cell.Element(ns + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(rawValue, out int sharedStringIndex)
            && sharedStrings.TryGetValue(sharedStringIndex, out string? sharedValue))
        {
            return sharedValue;
        }

        return rawValue;
    }

    private static bool IsColumnA(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return true;
        }

        return string.Equals(GetColumnName(cellReference!), "A", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetColumnName(string cellReference)
    {
        return new string(cellReference.TakeWhile(char.IsLetter).ToArray());
    }

    private static int? TryParseRowNumber(string? value)
    {
        return int.TryParse(value, out int rowNumber) ? rowNumber : null;
    }

    private static void WriteXlsxTemplate(string path)
    {
        using FileStream fileStream = File.Create(path);
        using ZipArchive archive = new(fileStream, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Worksets" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml());
    }

    private static string BuildWorksheetXml()
    {
        StringBuilder builder = new();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        builder.AppendLine("  <sheetData>");
        for (int index = 0; index < TemplateRows.Length; index++)
        {
            int rowNumber = index + 1;
            builder
                .Append("    <row r=\"")
                .Append(rowNumber)
                .Append("\"><c r=\"A")
                .Append(rowNumber)
                .Append("\" t=\"inlineStr\"><is><t>")
                .Append(SecurityElement.Escape(TemplateRows[index]))
                .AppendLine("</t></is></c></row>");
        }

        builder.AppendLine("  </sheetData>");
        builder.AppendLine("</worksheet>");
        return builder.ToString();
    }

    private static void WriteEntry(ZipArchive archive, string entryName, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string ReadFirstCell(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return string.Empty;
        }

        List<string> cells = new();
        bool inQuotes = false;
        char delimiter = DetectDelimiter(line);
        System.Text.StringBuilder current = new();
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && character == delimiter)
            {
                cells.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        cells.Add(current.ToString());
        return cells.Count == 0 ? string.Empty : cells[0];
    }

    private static char DetectDelimiter(string line)
    {
        int semicolonCount = line.Count(character => character == ';');
        int commaCount = line.Count(character => character == ',');
        return semicolonCount > commaCount ? ';' : ',';
    }
}
