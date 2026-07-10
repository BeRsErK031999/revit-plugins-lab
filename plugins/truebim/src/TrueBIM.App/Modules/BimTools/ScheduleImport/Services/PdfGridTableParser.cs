using System.IO;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class PdfGridTableParser : IPdfTableParser
{
    private readonly GridTableExtractionService extractionService;
    private readonly ITrueBimLogger? logger;

    public PdfGridTableParser(
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
            return PdfParserResult.FromError("Распознавание PDF отменено.");
        }
    }

    private PdfParserResult ParseCore(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return PdfParserResult.FromError("Выберите PDF-файл со спецификацией.");
        }

        string sourcePath = Path.GetFullPath(filePath);
        if (!File.Exists(sourcePath))
        {
            return PdfParserResult.FromError($"PDF-файл не найден: {sourcePath}");
        }

        try
        {
            List<ParsedTable> tables = [];
            using PdfDocument document = PdfDocument.Open(sourcePath);
            foreach (Page page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<TableSourceWord> words = page
                    .GetWords(NearestNeighbourWordExtractor.Instance)
                    .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                    .Select(word => new TableSourceWord(
                        word.Text,
                        word.BoundingBox.Left,
                        word.BoundingBox.Bottom,
                        word.BoundingBox.Right,
                        word.BoundingBox.Top))
                    .ToList();
                IReadOnlyList<TableSourceLine> lines = page.Paths
                    .SelectMany(path => path)
                    .SelectMany(subpath => subpath.Commands.OfType<PdfSubpath.Line>())
                    .Select(line => new TableSourceLine(
                        line.From.X,
                        line.From.Y,
                        line.To.X,
                        line.To.Y))
                    .ToList();
                ParsedTable? table = extractionService.Extract(
                    sourcePath,
                    page.Number,
                    words,
                    lines,
                    ["parser=PdfPig:grid"]);
                if (table is not null)
                {
                    tables.Add(table);
                }
            }

            if (tables.Count == 0)
            {
                return PdfParserResult.FromError(
                    "В PDF не найдена векторная табличная сетка. Для скана сначала выполните OCR или экспортируйте исходный лист в PDF с текстом и линиями.");
            }

            logger?.Info($"Schedule Import parsed PDF in-process. File='{Path.GetFileName(sourcePath)}'; Tables={tables.Count}.");
            return new PdfParserResult(
                tables,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
        catch (Exception exception)
        {
            logger?.Error("Schedule Import PDF parsing failed.", exception);
            return PdfParserResult.FromError($"Не удалось прочитать PDF: {exception.Message}");
        }
    }
}
