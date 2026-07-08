using System.Globalization;
using System.IO;
using System.Text;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashCsvImporter
{
    public ClashImportResult Import(string path)
    {
        Guard.NotNullOrWhiteSpace(path, nameof(path));

        if (!File.Exists(path))
        {
            return new ClashImportResult([], [$"Файл не найден: {path}"]);
        }

        string[] lines = File.ReadAllLines(path, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return new ClashImportResult([], ["CSV-файл пуст."]);
        }

        char delimiter = DetectDelimiter(lines[0]);
        List<string> headers = SplitRow(lines[0], delimiter);
        HeaderMap map = HeaderMap.Create(headers);
        List<string> errors = [];
        if (!map.HasAnyElementId && !map.HasPoint)
        {
            errors.Add("В CSV не найдены колонки ElementId1/ElementId2 или координаты X/Y/Z.");
        }

        List<ClashItem> items = [];
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        for (int lineIndex = 1; lineIndex < lines.Length; lineIndex++)
        {
            List<string> values = SplitRow(lines[lineIndex], delimiter);
            int rowNumber = lineIndex + 1;
            string fallbackId = $"Clash-{rowNumber - 1:0000}";
            string id = FirstNonEmpty(Get(values, map.ClashId), Get(values, map.ClashName), fallbackId);
            if (!usedIds.Add(id))
            {
                id = $"{id}_{rowNumber}";
                usedIds.Add(id);
            }

            long? elementId1 = ParseElementId(Get(values, map.ElementId1), rowNumber, "ElementId1", errors);
            long? elementId2 = ParseElementId(Get(values, map.ElementId2), rowNumber, "ElementId2", errors);
            double? x = ParseDouble(Get(values, map.X), rowNumber, "X", errors);
            double? y = ParseDouble(Get(values, map.Y), rowNumber, "Y", errors);
            double? z = ParseDouble(Get(values, map.Z), rowNumber, "Z", errors);
            string name = FirstNonEmpty(Get(values, map.ClashName), id);
            string comment = Get(values, map.Comment).Trim();
            ClashStatus status = ClashStatuses.Parse(Get(values, map.Status));

            if (!elementId1.HasValue && !elementId2.HasValue && (!x.HasValue || !y.HasValue || !z.HasValue))
            {
                errors.Add($"Строка {rowNumber}: нет ElementId и полной точки X/Y/Z.");
            }

            items.Add(new ClashItem(id, name, elementId1, elementId2, x, y, z, status, comment));
        }

        return new ClashImportResult(items, errors);
    }

    private static char DetectDelimiter(string header)
    {
        char[] delimiters = [';', ',', '\t'];
        return delimiters
            .Select(delimiter => new { Delimiter = delimiter, Count = SplitRow(header, delimiter).Count })
            .OrderByDescending(item => item.Count)
            .First()
            .Delimiter;
    }

    private static List<string> SplitRow(string row, char delimiter)
    {
        List<string> values = [];
        StringBuilder current = new();
        bool isQuoted = false;

        for (int index = 0; index < row.Length; index++)
        {
            char character = row[index];
            if (character == '"')
            {
                if (isQuoted && index + 1 < row.Length && row[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else
                {
                    isQuoted = !isQuoted;
                }

                continue;
            }

            if (character == delimiter && !isQuoted)
            {
                values.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString().Trim());
        return values;
    }

    private static long? ParseElementId(string value, int rowNumber, string columnName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = new(value.Where(character => char.IsDigit(character) || character == '-').ToArray());
        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long result) && result > 0)
        {
            return result;
        }

        errors.Add($"Строка {rowNumber}: {columnName} не является положительным ElementId.");
        return null;
    }

    private static double? ParseDouble(string value, int rowNumber, string columnName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().Replace(',', '.');
        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
            && !double.IsNaN(result)
            && !double.IsInfinity(result))
        {
            return result;
        }

        errors.Add($"Строка {rowNumber}: {columnName} не является числом.");
        return null;
    }

    private static string Get(IReadOnlyList<string> values, int index)
    {
        return index >= 0 && index < values.Count
            ? values[index]
            : string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private sealed class HeaderMap
    {
        private HeaderMap()
        {
        }

        public int ClashId { get; private set; } = -1;

        public int ClashName { get; private set; } = -1;

        public int ElementId1 { get; private set; } = -1;

        public int ElementId2 { get; private set; } = -1;

        public int X { get; private set; } = -1;

        public int Y { get; private set; } = -1;

        public int Z { get; private set; } = -1;

        public int Status { get; private set; } = -1;

        public int Comment { get; private set; } = -1;

        public bool HasAnyElementId => ElementId1 >= 0 || ElementId2 >= 0;

        public bool HasPoint => X >= 0 && Y >= 0 && Z >= 0;

        public static HeaderMap Create(IReadOnlyList<string> headers)
        {
            HeaderMap map = new();
            for (int index = 0; index < headers.Count; index++)
            {
                string normalized = Normalize(headers[index]);
                if (IsAny(normalized, "clashid", "id", "номерколлизии"))
                {
                    map.ClashId = index;
                }
                else if (IsAny(normalized, "clashname", "name", "clash", "имя", "название", "коллизия"))
                {
                    map.ClashName = index;
                }
                else if (IsAny(normalized, "elementid1", "element1id", "element1", "elementa", "id1", "элемент1", "элементид1"))
                {
                    map.ElementId1 = index;
                }
                else if (IsAny(normalized, "elementid2", "element2id", "element2", "elementb", "id2", "элемент2", "элементид2"))
                {
                    map.ElementId2 = index;
                }
                else if (normalized == "x")
                {
                    map.X = index;
                }
                else if (normalized == "y")
                {
                    map.Y = index;
                }
                else if (normalized == "z")
                {
                    map.Z = index;
                }
                else if (IsAny(normalized, "status", "статус"))
                {
                    map.Status = index;
                }
                else if (IsAny(normalized, "comment", "comments", "комментарий", "примечание"))
                {
                    map.Comment = index;
                }
            }

            if (map.ClashName < 0 && map.ClashId >= 0)
            {
                map.ClashName = map.ClashId;
            }

            return map;
        }

        private static bool IsAny(string value, params string[] candidates)
        {
            return candidates.Contains(value, StringComparer.Ordinal);
        }

        private static string Normalize(string value)
        {
            return new string(value
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
        }
    }
}
