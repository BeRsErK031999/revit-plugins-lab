using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public static class ScheduleImportParserFactory
{
    public static IPdfTableParser Create(ITrueBimLogger logger)
    {
        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        GridTableExtractionService extractionService = new();
        logger.Info("Schedule Import in-process PDF/DWG parsers configured.");
        return new ScheduleSourceTableParser(
            new PdfGridTableParser(extractionService, logger),
            new DwgGridTableParser(extractionService, logger),
            new ScheduleTableJsonReader());
    }
}
