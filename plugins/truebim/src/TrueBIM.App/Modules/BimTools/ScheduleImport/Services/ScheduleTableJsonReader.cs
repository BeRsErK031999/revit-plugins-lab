using System.IO;
using System.Text;
using System.Text.Json;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleTableJsonReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public IReadOnlyList<ParsedTable> ReadTables(string filePath)
    {
        PdfParserResult result = ReadParserResult(filePath);
        if (result.Tables.Count > 0)
        {
            return result.Tables;
        }

        throw new InvalidDataException("Schedule import JSON must contain a table object or a tables array.");
    }

    public PdfParserResult ReadParserResult(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("JSON path is required.", nameof(filePath));
        }

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Schedule import JSON is empty.");
        }

        try
        {
            ScheduleImportContract? root = JsonSerializer.Deserialize<ScheduleImportContract>(json, SerializerOptions);
            if (root is not null
                && (root.Tables is not null || root.Warnings is not null || root.Errors is not null))
            {
                IReadOnlyList<ParsedTable> tables = root.Tables is { Count: > 0 }
                    ? root.Tables.Select((table, index) => MapTable(table, filePath, index)).ToList()
                    : Array.Empty<ParsedTable>();
                return new PdfParserResult(
                    tables,
                    NormalizeMessages(root.Warnings),
                    NormalizeMessages(root.Errors));
            }

            ParsedTableContract? singleTable = JsonSerializer.Deserialize<ParsedTableContract>(json, SerializerOptions);
            if (singleTable is not null)
            {
                return new PdfParserResult(
                    [MapTable(singleTable, filePath, 0)],
                    Array.Empty<string>(),
                    Array.Empty<string>());
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Schedule import JSON is not valid.", exception);
        }

        throw new InvalidDataException("Schedule import JSON must contain a table object or a tables array.");
    }

    private static IReadOnlyList<string> NormalizeMessages(List<string>? messages)
    {
        return messages?
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message.Trim())
            .Distinct(StringComparer.CurrentCulture)
            .ToArray() ?? Array.Empty<string>();
    }

    private static ParsedTable MapTable(ParsedTableContract contract, string fallbackSourcePath, int tableIndex)
    {
        string sourcePath = string.IsNullOrWhiteSpace(contract.SourceFilePath)
            ? fallbackSourcePath
            : contract.SourceFilePath!.Trim();
        IReadOnlyList<string> columns = NormalizeColumns(contract.Columns, contract.Rows, contract.Cells);
        IReadOnlyList<ParsedCell> cells = contract.Cells is { Count: > 0 }
            ? MapCells(contract.Cells, columns.Count)
            : BuildCellsFromRows(contract.Rows, columns.Count);
        IReadOnlyList<ParsedRow> rows = contract.Rows is { Count: > 0 }
            ? BuildRows(contract.Rows, columns.Count)
            : BuildRowsFromCells(cells, columns.Count);
        IReadOnlyList<string> warnings = contract.Warnings?
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Select(warning => warning.Trim())
            .ToArray() ?? Array.Empty<string>();

        return new ParsedTable(
            sourcePath,
            contract.PageNumber <= 0 ? tableIndex + 1 : contract.PageNumber,
            rows,
            columns,
            cells,
            NormalizeConfidence(contract.Confidence),
            warnings);
    }

    private static IReadOnlyList<string> NormalizeColumns(
        List<string>? columns,
        List<List<string>>? rows,
        List<ParsedCellContract>? cells)
    {
        if (columns is { Count: > 0 })
        {
            return columns
                .Select((column, index) => string.IsNullOrWhiteSpace(column) ? $"Колонка {index + 1}" : column.Trim())
                .ToList();
        }

        int columnCount = 0;
        if (rows is { Count: > 0 })
        {
            columnCount = rows.Max(row => row.Count);
        }
        else if (cells is { Count: > 0 })
        {
            columnCount = cells.Max(cell => Math.Max(0, cell.ColumnIndex) + Math.Max(1, cell.ColumnSpan));
        }

        return Enumerable.Range(1, columnCount)
            .Select(index => $"Колонка {index}")
            .ToList();
    }

    private static IReadOnlyList<ParsedCell> MapCells(IReadOnlyList<ParsedCellContract> cells, int columnCount)
    {
        List<ParsedCell> result = [];
        for (int index = 0; index < cells.Count; index++)
        {
            ParsedCellContract cell = cells[index];
            int rowIndex = Math.Max(0, cell.RowIndex);
            int columnIndex = Math.Max(0, cell.ColumnIndex);
            result.Add(new ParsedCell(
                rowIndex,
                columnIndex,
                Math.Max(1, cell.RowSpan),
                Math.Max(1, cell.ColumnSpan),
                cell.Text?.Trim() ?? string.Empty,
                cell.BoundingBox is null
                    ? null
                    : new ParsedCellBoundingBox(
                        cell.BoundingBox.X,
                        cell.BoundingBox.Y,
                        Math.Max(0, cell.BoundingBox.Width),
                        Math.Max(0, cell.BoundingBox.Height)),
                NormalizeConfidence(cell.Confidence),
                cell.IsHeader || rowIndex == 0));
        }

        if (columnCount > 0)
        {
            return result
                .Where(cell => cell.ColumnIndex < columnCount)
                .ToList();
        }

        return result;
    }

    private static IReadOnlyList<ParsedCell> BuildCellsFromRows(List<List<string>>? rows, int columnCount)
    {
        if (rows is null)
        {
            return Array.Empty<ParsedCell>();
        }

        List<ParsedCell> cells = [];
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            int count = columnCount == 0 ? row.Count : columnCount;
            for (int columnIndex = 0; columnIndex < count; columnIndex++)
            {
                string text = columnIndex < row.Count ? row[columnIndex] : string.Empty;
                cells.Add(new ParsedCell(
                    rowIndex,
                    columnIndex,
                    1,
                    1,
                    text?.Trim() ?? string.Empty,
                    null,
                    1.0,
                    rowIndex == 0));
            }
        }

        return cells;
    }

    private static IReadOnlyList<ParsedRow> BuildRows(List<List<string>>? rows, int columnCount)
    {
        if (rows is null)
        {
            return Array.Empty<ParsedRow>();
        }

        return rows
            .Select((row, index) => new ParsedRow(
                index,
                Enumerable.Range(0, columnCount == 0 ? row.Count : columnCount)
                    .Select(columnIndex => columnIndex < row.Count ? row[columnIndex]?.Trim() ?? string.Empty : string.Empty)
                    .ToList()))
            .ToList();
    }

    private static IReadOnlyList<ParsedRow> BuildRowsFromCells(IReadOnlyList<ParsedCell> cells, int columnCount)
    {
        if (cells.Count == 0)
        {
            return Array.Empty<ParsedRow>();
        }

        int rowCount = cells.Max(cell => cell.RowIndex + Math.Max(1, cell.RowSpan));
        string[,] values = new string[rowCount, columnCount];
        foreach (ParsedCell cell in cells)
        {
            if (cell.RowIndex >= rowCount || cell.ColumnIndex >= columnCount)
            {
                continue;
            }

            values[cell.RowIndex, cell.ColumnIndex] = cell.Text;
        }

        List<ParsedRow> rows = [];
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            List<string> row = [];
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                row.Add(values[rowIndex, columnIndex] ?? string.Empty);
            }

            rows.Add(new ParsedRow(rowIndex, row));
        }

        return rows;
    }

    private static double NormalizeConfidence(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        if (value <= 0)
        {
            return 0;
        }

        return value > 1 ? 1 : value;
    }

    private sealed class ScheduleImportContract
    {
        public List<ParsedTableContract>? Tables { get; set; }

        public List<string>? Warnings { get; set; }

        public List<string>? Errors { get; set; }
    }

    private sealed class ParsedTableContract
    {
        public string? SourceFilePath { get; set; }

        public int PageNumber { get; set; }

        public List<string>? Columns { get; set; }

        public List<List<string>>? Rows { get; set; }

        public List<ParsedCellContract>? Cells { get; set; }

        public double Confidence { get; set; } = 1.0;

        public List<string>? Warnings { get; set; }
    }

    private sealed class ParsedCellContract
    {
        public int RowIndex { get; set; }

        public int ColumnIndex { get; set; }

        public int RowSpan { get; set; } = 1;

        public int ColumnSpan { get; set; } = 1;

        public string? Text { get; set; }

        public ParsedCellBoundingBoxContract? BoundingBox { get; set; }

        public double Confidence { get; set; } = 1.0;

        public bool IsHeader { get; set; }
    }

    private sealed class ParsedCellBoundingBoxContract
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }
}
