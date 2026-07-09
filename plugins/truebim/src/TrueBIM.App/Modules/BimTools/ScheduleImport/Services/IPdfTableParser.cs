using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public interface IPdfTableParser
{
    Task<PdfParserResult> ParseAsync(string filePath, CancellationToken cancellationToken);
}
