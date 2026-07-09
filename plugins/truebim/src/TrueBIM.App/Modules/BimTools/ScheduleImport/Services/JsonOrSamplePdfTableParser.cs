using System.IO;
using System.Text.Json;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class JsonOrSamplePdfTableParser : IPdfTableParser
{
    private readonly ScheduleTableJsonReader jsonReader;

    public JsonOrSamplePdfTableParser(ScheduleTableJsonReader jsonReader)
    {
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
    }

    public Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(PdfParserResult.FromError("Выберите PDF или JSON-файл с таблицей."));
        }

        string extension = Path.GetExtension(filePath);
        try
        {
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<ParsedTable> tables = jsonReader.ReadTables(filePath);
                return Task.FromResult(new PdfParserResult(
                    tables,
                    ["Загружена промежуточная JSON-модель таблицы."],
                    Array.Empty<string>()));
            }

            if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                ParsedTable table = ScheduleImportSampleTables.CreatePipeSchedule(filePath);
                return Task.FromResult(new PdfParserResult(
                    [table],
                    ["Реальный PDF parser пока не подключён: используется тестовая таблица для проверки Revit Drafting Table Mode."],
                    Array.Empty<string>()));
            }

            if (extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(PdfParserResult.FromError("DWG parsing is planned. Для MVP экспортируйте лист в PDF или загрузите JSON-модель таблицы."));
            }

            return Task.FromResult(PdfParserResult.FromError("Поддерживаются PDF, DWG-заглушка и JSON-модель таблицы."));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        {
            return Task.FromResult(PdfParserResult.FromError(exception.Message));
        }
    }
}
