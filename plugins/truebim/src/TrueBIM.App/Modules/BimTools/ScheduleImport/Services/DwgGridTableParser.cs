using System.IO;
using System.Text.RegularExpressions;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using CSMath;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class DwgGridTableParser : IPdfTableParser
{
    private readonly GridTableExtractionService extractionService;
    private readonly ITrueBimLogger? logger;

    public DwgGridTableParser(
        GridTableExtractionService extractionService,
        ITrueBimLogger? logger = null)
    {
        this.extractionService = extractionService ?? throw new ArgumentNullException(nameof(extractionService));
        this.logger = logger;
    }

    public async Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => ParseCore(filePath, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return PdfParserResult.FromError("Распознавание DWG отменено.");
        }
    }

    private PdfParserResult ParseCore(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return PdfParserResult.FromError("Выберите DWG-файл со спецификацией.");
        }

        string sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
        {
            return PdfParserResult.FromError($"DWG-файл не найден: {sourcePath}");
        }

        try
        {
            List<string> readerWarnings = [];
            CadDocument document = DwgReader.Read(
                sourcePath,
                (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Message))
                    {
                        readerWarnings.Add(args.Message.Trim());
                    }
                });
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<Entity> entities = FlattenEntities(document, readerWarnings, cancellationToken);
            List<ParsedTable> nativeTables = entities
                .OfType<TableEntity>()
                .Select((table, index) => MapNativeTable(sourcePath, table, index + 1, readerWarnings))
                .Where(table => table is not null)
                .Cast<ParsedTable>()
                .ToList();
            if (nativeTables.Count > 0)
            {
                logger?.Info($"Schedule Import parsed native DWG tables. File='{Path.GetFileName(sourcePath)}'; Tables={nativeTables.Count}.");
                return new PdfParserResult(nativeTables, readerWarnings.Distinct().ToList(), Array.Empty<string>());
            }

            List<TableSourceWord> words = [];
            List<TableSourceLine> lines = [];
            foreach (Entity entity in entities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppendEntityGeometry(entity, words, lines);
            }

            ParsedTable? gridTable = extractionService.Extract(
                sourcePath,
                1,
                words,
                lines,
                ["parser=ACadSharp:grid", .. readerWarnings.Distinct()]);
            if (gridTable is null)
            {
                return PdfParserResult.FromError(
                    "В DWG не найдена таблица с текстом и линейной сеткой. Проверьте, что спецификация находится в model space или paper space и не представлена только растровым изображением.");
            }

            logger?.Info($"Schedule Import parsed DWG grid in-process. File='{Path.GetFileName(sourcePath)}'; Rows={gridTable.RowCount}; Columns={gridTable.ColumnCount}.");
            return new PdfParserResult([gridTable], readerWarnings.Distinct().ToList(), Array.Empty<string>());
        }
        catch (Exception exception)
        {
            logger?.Error("Schedule Import DWG parsing failed.", exception);
            return PdfParserResult.FromError($"Не удалось прочитать DWG: {exception.Message}");
        }
    }

    private static IReadOnlyList<Entity> FlattenEntities(
        CadDocument document,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        List<Entity> result = [];
        IEnumerable<Entity> roots = document.ModelSpace.Entities.Concat(document.PaperSpace.Entities);
        foreach (Entity entity in roots)
        {
            FlattenEntity(entity, result, warnings, cancellationToken, depth: 0);
        }

        return result;
    }

    private static void FlattenEntity(
        Entity entity,
        List<Entity> result,
        List<string> warnings,
        CancellationToken cancellationToken,
        int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (entity is TableEntity)
        {
            result.Add(entity);
            return;
        }

        if (entity is not Insert insert || depth >= 8)
        {
            result.Add(entity);
            return;
        }

        try
        {
            foreach (Entity child in insert.Explode())
            {
                FlattenEntity(child, result, warnings, cancellationToken, depth + 1);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
            warnings.Add($"Не удалось раскрыть DWG-блок '{insert.Block?.Name ?? insert.ObjectName}': {exception.Message}");
        }
    }

    private static ParsedTable? MapNativeTable(
        string sourcePath,
        TableEntity table,
        int tableIndex,
        IReadOnlyList<string> readerWarnings)
    {
        int rowCount = table.Rows.Count;
        int columnCount = table.Columns.Count;
        if (rowCount < 2 || columnCount < 2)
        {
            return null;
        }

        double[] columnOffsets = BuildOffsets(table.Columns.Select(column => Math.Max(1e-6, column.Width)).ToList());
        double[] rowOffsets = BuildOffsets(table.Rows.Select(row => Math.Max(1e-6, row.Height)).ToList());
        List<ParsedRow> rows = [];
        List<ParsedCell> cells = [];
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            List<string> values = [];
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                TableEntity.Cell cell = table.GetCell(rowIndex, columnIndex);
                values.Add(GetCellText(cell));
            }

            rows.Add(new ParsedRow(rowIndex, values));
        }

        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                TableEntity.CellRange? merged = table.MergedCellRanges.FirstOrDefault(range =>
                    rowIndex >= range.TopRowIndex
                    && rowIndex <= range.BottomRowIndex
                    && columnIndex >= range.LeftColumnIndex
                    && columnIndex <= range.RightColumnIndex);
                if (merged is not null
                    && (rowIndex != merged.TopRowIndex || columnIndex != merged.LeftColumnIndex))
                {
                    continue;
                }

                int rowSpan = merged is null ? 1 : merged.BottomRowIndex - merged.TopRowIndex + 1;
                int columnSpan = merged is null ? 1 : merged.RightColumnIndex - merged.LeftColumnIndex + 1;
                cells.Add(new ParsedCell(
                    rowIndex,
                    columnIndex,
                    rowSpan,
                    columnSpan,
                    rows[rowIndex].Values[columnIndex],
                    new ParsedCellBoundingBox(
                        columnOffsets[columnIndex],
                        rowOffsets[rowIndex],
                        columnOffsets[Math.Min(columnCount, columnIndex + columnSpan)] - columnOffsets[columnIndex],
                        rowOffsets[Math.Min(rowCount, rowIndex + rowSpan)] - rowOffsets[rowIndex]),
                    0.98,
                    rowIndex == 0));
            }
        }

        IReadOnlyList<string> columns = MakeColumnNames(rows[0].Values);
        return new ParsedTable(
            sourcePath,
            tableIndex,
            rows,
            columns,
            cells,
            0.98,
            ["parser=ACadSharp:table", .. readerWarnings.Distinct()]);
    }

    private static string GetCellText(TableEntity.Cell cell)
    {
        IEnumerable<TableEntity.CellContent> contents = cell.HasMultipleContent
            ? cell.Contents
            : cell.Content is null
                ? Array.Empty<TableEntity.CellContent>()
                : [cell.Content];
        return NormalizeText(string.Join(
            Environment.NewLine,
            contents
                .Select(content => content.CadValue?.FormattedValue ?? content.CadValue?.Value?.ToString())
                .Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static double[] BuildOffsets(IReadOnlyList<double> sizes)
    {
        double[] result = new double[sizes.Count + 1];
        for (int index = 0; index < sizes.Count; index++)
        {
            result[index + 1] = result[index] + sizes[index];
        }

        return result;
    }

    private static IReadOnlyList<string> MakeColumnNames(IReadOnlyList<string> header)
    {
        HashSet<string> used = new(StringComparer.CurrentCultureIgnoreCase);
        List<string> result = [];
        for (int index = 0; index < header.Count; index++)
        {
            string baseName = NormalizeText(header[index]);
            if (baseName.Length == 0)
            {
                baseName = $"Колонка {index + 1}";
            }

            string name = baseName;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = $"{baseName} {suffix++}";
            }

            result.Add(name);
        }

        return result;
    }

    private static void AppendEntityGeometry(
        Entity entity,
        List<TableSourceWord> words,
        List<TableSourceLine> lines)
    {
        switch (entity)
        {
            case ACadSharp.Entities.Line line:
                lines.Add(new TableSourceLine(
                    line.StartPoint.X,
                    line.StartPoint.Y,
                    line.EndPoint.X,
                    line.EndPoint.Y));
                break;

            case LwPolyline polyline:
                AppendPolyline(
                    polyline.Vertices.Select(vertex => new XY(vertex.Location.X, vertex.Location.Y)).ToList(),
                    polyline.IsClosed,
                    lines);
                break;

            case Polyline2D polyline:
                AppendPolyline(
                    polyline.Vertices.Select(vertex => new XY(vertex.Location.X, vertex.Location.Y)).ToList(),
                    polyline.IsClosed,
                    lines);
                break;

            case MText text:
                AddWord(entity, text.PlainText, text.InsertPoint, Math.Max(text.Height, text.VerticalHeight), words);
                break;

            case TextEntity text:
                AddWord(entity, text.Value, text.InsertPoint, text.Height, words);
                break;
        }
    }

    private static void AppendPolyline(
        IReadOnlyList<XY> vertices,
        bool closed,
        List<TableSourceLine> lines)
    {
        for (int index = 1; index < vertices.Count; index++)
        {
            lines.Add(new TableSourceLine(
                vertices[index - 1].X,
                vertices[index - 1].Y,
                vertices[index].X,
                vertices[index].Y));
        }

        if (closed && vertices.Count > 2)
        {
            lines.Add(new TableSourceLine(
                vertices[vertices.Count - 1].X,
                vertices[vertices.Count - 1].Y,
                vertices[0].X,
                vertices[0].Y));
        }
    }

    private static void AddWord(
        Entity entity,
        string? value,
        XYZ insertionPoint,
        double textHeight,
        List<TableSourceWord> words)
    {
        string text = NormalizeText(value);
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            BoundingBox box = entity.GetBoundingBox();
            if (!double.IsNaN(box.Min.X)
                && !double.IsNaN(box.Min.Y)
                && !double.IsNaN(box.Max.X)
                && !double.IsNaN(box.Max.Y))
            {
                words.Add(new TableSourceWord(text, box.Min.X, box.Min.Y, box.Max.X, box.Max.Y));
                return;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException)
        {
        }

        double height = Math.Max(1e-6, textHeight);
        double width = height * Math.Max(1, text.Length) * 0.55;
        words.Add(new TableSourceWord(
            text,
            insertionPoint.X,
            insertionPoint.Y,
            insertionPoint.X + width,
            insertionPoint.Y + height));
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value!;
        return string.Join(
            Environment.NewLine,
            normalized
                .Replace("\\P", Environment.NewLine)
                .Replace("\u00a0", " ")
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                .Where(line => line.Length > 0));
    }
}
