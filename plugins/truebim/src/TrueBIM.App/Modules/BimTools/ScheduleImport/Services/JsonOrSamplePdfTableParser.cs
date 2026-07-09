using System.IO;
using System.Text.Json;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class JsonOrSamplePdfTableParser : IPdfTableParser
{
    private readonly ScheduleTableJsonReader jsonReader;
    private readonly IPdfTableParser? pdfParser;
    private readonly ITrueBimLogger? logger;

    public JsonOrSamplePdfTableParser(
        ScheduleTableJsonReader jsonReader,
        IPdfTableParser? pdfParser = null,
        ITrueBimLogger? logger = null)
    {
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
        this.pdfParser = pdfParser;
        this.logger = logger;
    }

    public async Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return PdfParserResult.FromError("Выберите PDF или JSON-файл с таблицей.");
        }

        string extension = Path.GetExtension(filePath);
        try
        {
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                PdfParserResult result = jsonReader.ReadParserResult(filePath);
                return new PdfParserResult(
                    result.Tables,
                    ["Загружена промежуточная JSON-модель таблицы.", .. result.Warnings],
                    result.Errors);
            }

            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                if (pdfParser is not null)
                {
                    return await pdfParser.ParseAsync(filePath, cancellationToken).ConfigureAwait(false);
                }

                logger?.Warning("Schedule Import PDF parser is not configured. Using sample table.");
                ParsedTable table = ScheduleImportSampleTables.CreatePipeSchedule(filePath);
                return new PdfParserResult(
                    [table],
                    ["PDF parser не подключён: используется тестовая таблица для проверки Revit Drafting Table Mode."],
                    Array.Empty<string>());
            }

            if (extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                return PdfParserResult.FromWarning(
                    "DWG пока не распознаётся напрямую. Для первого релиза экспортируйте лист DWG в PDF или загрузите промежуточную JSON-модель таблицы.");
            }

            return PdfParserResult.FromError("Поддерживаются PDF и JSON. DWG пока доступен только через предварительную конвертацию в PDF.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return PdfParserResult.FromError(exception.Message);
        }
    }
}
