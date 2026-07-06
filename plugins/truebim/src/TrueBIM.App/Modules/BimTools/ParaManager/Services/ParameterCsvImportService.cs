using System.IO;
using System.Text;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class ParameterCsvImportService
{
    private static readonly string[] RequiredHeaders =
    [
        "ParameterName",
        "SharedGroup",
        "BindingType",
        "Categories",
        "GroupUnder",
        "DataType",
        "Visible",
        "UserModifiable",
        "Description"
    ];

    public IReadOnlyList<ParameterImportRow> Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("CSV path is empty.", nameof(path));
        }

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0)
        {
            return [];
        }

        char delimiter = DetectDelimiter(lines[0]);
        IReadOnlyList<string> header = ParseLine(lines[0], delimiter);
        Dictionary<string, int> headers = BuildHeaderMap(header);
        ValidateRequiredHeaders(headers);

        List<ParameterImportRow> rows = [];
        for (int index = 1; index < lines.Length; index++)
        {
            IReadOnlyList<string> cells = ParseLine(lines[index], delimiter);
            rows.Add(new ParameterImportRow(
                index + 1,
                GetCell(cells, headers, "ParameterName"),
                GetCell(cells, headers, "SharedGroup"),
                GetCell(cells, headers, "BindingType"),
                GetCell(cells, headers, "Categories"),
                GetCell(cells, headers, "GroupUnder"),
                GetCell(cells, headers, "DataType"),
                GetCell(cells, headers, "Visible"),
                GetCell(cells, headers, "UserModifiable"),
                GetCell(cells, headers, "Description")));
        }

        return rows;
    }

    public string CreateTemplate()
    {
        return string.Join(Environment.NewLine, [
            string.Join(";", RequiredHeaders),
            "BIM_Код помещения;BIM;Instance;Rooms;Identity Data;Text;true;true;Код помещения по BIM-стандарту",
            "BIM_Раздел;BIM;Instance;Walls,Doors,Windows;Identity Data;Text;true;true;Раздел модели",
            "BIM_Этаж;BIM;Instance;Walls,Doors,Windows,Floors;Identity Data;Text;true;true;Этаж элемента",
            "BIM_Стадия;BIM;Instance;Walls,Doors,Windows;Identity Data;Text;true;true;Стадия проектирования",
            "BIM_Проверено;BIM;Instance;Rooms,Walls;Identity Data;YesNo;true;true;Параметр контроля"
        ]);
    }

    private static Dictionary<string, int> BuildHeaderMap(IReadOnlyList<string> header)
    {
        Dictionary<string, int> headers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < header.Count; index++)
        {
            string canonical = CanonicalHeader(header[index]);
            if (!headers.ContainsKey(canonical))
            {
                headers.Add(canonical, index);
            }
        }

        return headers;
    }

    private static void ValidateRequiredHeaders(Dictionary<string, int> headers)
    {
        string[] missingHeaders = RequiredHeaders
            .Where(header => !headers.ContainsKey(header))
            .ToArray();
        if (missingHeaders.Length > 0)
        {
            throw new InvalidOperationException($"CSV не содержит обязательные колонки: {string.Join(", ", missingHeaders)}.");
        }
    }

    private static string GetCell(IReadOnlyList<string> cells, Dictionary<string, int> headers, string header)
    {
        int index = headers[header];
        return index < cells.Count ? cells[index] : string.Empty;
    }

    private static string CanonicalHeader(string header)
    {
        string normalized = header.Trim().Replace(" ", string.Empty);
        return normalized.ToLowerInvariant() switch
        {
            "name" or "parameter" or "parametername" or "имяпараметра" => "ParameterName",
            "group" or "sharedgroup" or "sharedparametergroup" or "группа" => "SharedGroup",
            "binding" or "bindingtype" or "типпривязки" => "BindingType",
            "category" or "categories" or "категории" => "Categories",
            "groupunder" or "parametergroup" or "propertygroup" or "группасвойств" => "GroupUnder",
            "type" or "datatype" or "parametertype" or "типданных" => "DataType",
            "visible" or "видимый" or "видимость" => "Visible",
            "usermodifiable" or "modifiable" or "изменяемый" => "UserModifiable",
            "description" or "описание" => "Description",
            _ => header.Trim()
        };
    }

    private static IReadOnlyList<string> ParseLine(string line, char delimiter)
    {
        List<string> cells = [];
        StringBuilder current = new();
        bool inQuotes = false;

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
        return cells;
    }

    private static char DetectDelimiter(string headerLine)
    {
        int semicolonCount = headerLine.Count(character => character == ';');
        int commaCount = headerLine.Count(character => character == ',');
        return semicolonCount >= commaCount ? ';' : ',';
    }
}
