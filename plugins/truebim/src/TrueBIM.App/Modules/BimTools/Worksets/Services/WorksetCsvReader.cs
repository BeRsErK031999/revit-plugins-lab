using TrueBIM.App.Modules.BimTools.Worksets.Models;
using System.IO;

namespace TrueBIM.App.Modules.BimTools.Worksets.Services;

public sealed class WorksetCsvReader
{
    public IReadOnlyList<WorksetImportRow> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("CSV path is empty.", nameof(path));
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
