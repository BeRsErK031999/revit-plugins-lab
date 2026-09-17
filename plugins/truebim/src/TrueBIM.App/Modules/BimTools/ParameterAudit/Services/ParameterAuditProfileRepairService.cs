using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.Services;

public sealed record ParameterAuditRepairResult(
    string RepairedPath,
    int MergedColumnCount,
    int MovedMarkerCount);

public sealed class ParameterAuditProfileRepairService
{
    public ParameterAuditRepairResult CreateRepairedCopy(
        string sourcePath,
        IReadOnlyList<ParameterAuditProfileFix> fixes)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Исходная таблица проверки не найдена.", sourcePath);
        }

        if (!Path.GetExtension(sourcePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Автоисправление поддерживается только для XLSX. CSV можно исправить непосредственно в Excel.");
        }

        IReadOnlyList<ParameterAuditProfileFix> applicableFixes = fixes
            .GroupBy(fix => fix.DuplicateColumnIndex)
            .Select(group => group.First())
            .OrderBy(fix => fix.DuplicateColumnIndex)
            .ToList();
        if (applicableFixes.Count == 0)
        {
            throw new InvalidOperationException("В таблице нет ошибок, доступных для автоисправления.");
        }

        string repairedPath = BuildRepairedPath(sourcePath);
        CopyWithSharedRead(sourcePath, repairedPath);
        try
        {
            int movedMarkerCount = RepairWorkbook(repairedPath, applicableFixes);
            return new ParameterAuditRepairResult(
                repairedPath,
                applicableFixes.Count,
                movedMarkerCount);
        }
        catch
        {
            if (File.Exists(repairedPath))
            {
                File.Delete(repairedPath);
            }

            throw;
        }
    }

    private static int RepairWorkbook(
        string path,
        IReadOnlyList<ParameterAuditProfileFix> fixes)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Update, false);
        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        ZipArchiveEntry sheetEntry = ResolveFirstWorksheet(archive)
            ?? throw new InvalidOperationException("XLSX не содержит листов.");

        XDocument sheet;
        using (Stream readStream = sheetEntry.Open())
        {
            sheet = XDocument.Load(readStream);
        }

        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Dictionary<int, XElement> rows = sheet
            .Descendants(ns + "row")
            .Select((row, index) => new
            {
                Element = row,
                LineNumber = int.TryParse(
                    (string?)row.Attribute("r"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedLine)
                    ? parsedLine
                    : index + 1
            })
            .ToDictionary(item => item.LineNumber, item => item.Element);

        ValidateFixSafety(rows, sharedStrings, fixes, ns);
        int movedMarkerCount = 0;
        foreach (ParameterAuditProfileFix fix in fixes)
        {
            XElement headerRow = rows.TryGetValue(fix.HeaderLineNumber, out XElement? existingHeader)
                ? existingHeader
                : throw new InvalidOperationException(
                    $"Строка заголовков {fix.HeaderLineNumber} не найдена в XLSX.");
            string primaryHeaderCell = $"{GetColumnName(fix.PrimaryColumnIndex)}{fix.HeaderLineNumber}";
            SetCellText(
                headerRow,
                fix.DuplicateColumnIndex,
                fix.HeaderLineNumber,
                $"TrueBIM: объединено с {primaryHeaderCell}",
                ns);

            foreach (KeyValuePair<int, XElement> row in rows.Where(row => row.Key > fix.HeaderLineNumber))
            {
                XElement? duplicateCell = FindCell(row.Value, fix.DuplicateColumnIndex);
                if (duplicateCell is null)
                {
                    continue;
                }

                string duplicateValue = ReadXlsxCell(duplicateCell, sharedStrings, ns).Trim();
                if (IsRequiredMarker(duplicateValue))
                {
                    XElement? primaryCell = FindCell(row.Value, fix.PrimaryColumnIndex);
                    string primaryValue = primaryCell is null
                        ? string.Empty
                        : ReadXlsxCell(primaryCell, sharedStrings, ns).Trim();
                    if (!IsRequiredMarker(primaryValue))
                    {
                        SetCellText(
                            row.Value,
                            fix.PrimaryColumnIndex,
                            row.Key,
                            "+",
                            ns,
                            duplicateCell);
                        movedMarkerCount++;
                    }
                }

                SetCellText(
                    row.Value,
                    fix.DuplicateColumnIndex,
                    row.Key,
                    string.Empty,
                    ns,
                    duplicateCell);
            }

        }

        using Stream writeStream = sheetEntry.Open();
        writeStream.SetLength(0);
        sheet.Save(writeStream, SaveOptions.DisableFormatting);
        return movedMarkerCount;
    }

    private static void ValidateFixSafety(
        IReadOnlyDictionary<int, XElement> rows,
        IReadOnlyList<string> sharedStrings,
        IReadOnlyList<ParameterAuditProfileFix> fixes,
        XNamespace ns)
    {
        foreach (ParameterAuditProfileFix fix in fixes)
        {
            foreach (KeyValuePair<int, XElement> row in rows.Where(row => row.Key > fix.HeaderLineNumber))
            {
                XElement? cell = FindCell(row.Value, fix.DuplicateColumnIndex);
                if (cell is null)
                {
                    continue;
                }

                string value = ReadXlsxCell(cell, sharedStrings, ns).Trim();
                if (!string.IsNullOrWhiteSpace(value)
                    && !IsRequiredMarker(value)
                    && value != "0"
                    && value != "-")
                {
                    string address = $"{GetColumnName(fix.DuplicateColumnIndex)}{row.Key}";
                    throw new InvalidOperationException(
                        $"Колонку нельзя объединить автоматически: ячейка {address} содержит «{value}», а ожидались +, 0, - или пустое значение.");
                }
            }
        }
    }

    private static void CopyWithSharedRead(string sourcePath, string destinationPath)
    {
        using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        source.CopyTo(destination);
    }

    private static string BuildRepairedPath(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException("Не удалось определить папку исходного XLSX.");
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string candidate = Path.Combine(directory, $"{fileName}.truebim-fixed-{timestamp}.xlsx");
        int suffix = 2;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{fileName}.truebim-fixed-{timestamp}-{suffix}.xlsx");
            suffix++;
        }

        return candidate;
    }

    private static XElement? FindCell(XElement row, int columnIndex)
    {
        return row.Elements()
            .FirstOrDefault(cell => GetColumnIndex((string?)cell.Attribute("r")) == columnIndex);
    }

    private static void SetCellText(
        XElement row,
        int columnIndex,
        int lineNumber,
        string value,
        XNamespace ns,
        XElement? styleSource = null)
    {
        XElement? cell = FindCell(row, columnIndex);
        if (cell is null)
        {
            string reference = $"{GetColumnName(columnIndex)}{lineNumber}";
            cell = new XElement(ns + "c", new XAttribute("r", reference));
            XAttribute? style = styleSource?.Attribute("s");
            if (style is not null)
            {
                cell.SetAttributeValue("s", style.Value);
            }

            XElement? nextCell = row.Elements(ns + "c")
                .FirstOrDefault(candidate =>
                    (GetColumnIndex((string?)candidate.Attribute("r")) ?? int.MaxValue) > columnIndex);
            if (nextCell is null)
            {
                row.Add(cell);
            }
            else
            {
                nextCell.AddBeforeSelf(cell);
            }
        }

        cell.SetAttributeValue("t", "inlineStr");
        cell.Elements(ns + "f").Remove();
        cell.Elements(ns + "v").Remove();
        cell.Elements(ns + "is").Remove();
        cell.Add(new XElement(
            ns + "is",
            new XElement(
                ns + "t",
                new XAttribute(XNamespace.Xml + "space", "preserve"),
                value)));
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

        return value;
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

    private static string GetColumnName(int zeroBasedColumn)
    {
        int value = zeroBasedColumn + 1;
        System.Text.StringBuilder result = new();
        while (value > 0)
        {
            value--;
            result.Insert(0, (char)('A' + (value % 26)));
            value /= 26;
        }

        return result.ToString();
    }

    private static bool IsRequiredMarker(string value)
    {
        string normalized = value.Trim();
        return normalized == "+" || normalized == "＋";
    }
}
