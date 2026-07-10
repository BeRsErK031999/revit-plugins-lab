using System.IO;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleSourceTableParser : IPdfTableParser
{
    private readonly IPdfTableParser pdfParser;
    private readonly IPdfTableParser dwgParser;
    private readonly ScheduleTableJsonReader jsonReader;

    public ScheduleSourceTableParser(
        IPdfTableParser pdfParser,
        IPdfTableParser dwgParser,
        ScheduleTableJsonReader jsonReader)
    {
        this.pdfParser = pdfParser ?? throw new ArgumentNullException(nameof(pdfParser));
        this.dwgParser = dwgParser ?? throw new ArgumentNullException(nameof(dwgParser));
        this.jsonReader = jsonReader ?? throw new ArgumentNullException(nameof(jsonReader));
    }

    public Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult(PdfParserResult.FromError("Выберите PDF или DWG со спецификацией."));
        }

        string extension = Path.GetExtension(filePath);
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return pdfParser.ParseAsync(filePath, cancellationToken);
        }

        if (extension.Equals(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            return dwgParser.ParseAsync(filePath, cancellationToken);
        }

        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(jsonReader.ReadParserResult(filePath));
        }

        return Task.FromResult(PdfParserResult.FromError("Поддерживаются файлы PDF, DWG и промежуточные JSON-модели."));
    }
}
