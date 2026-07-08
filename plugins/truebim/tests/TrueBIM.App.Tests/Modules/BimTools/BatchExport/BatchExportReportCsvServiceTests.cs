using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.BimTools.BatchExport.Services;
using Xunit;

namespace TrueBIM.App.Tests.Modules.BimTools.BatchExport;

public sealed class BatchExportReportCsvServiceTests
{
    [Fact]
    public void Format_WritesHeaderAndEscapesRows()
    {
        BatchExportReportCsvService service = new();

        string csv = service.Format(
        [
            new BatchExportReportRow(
                "A-101",
                "Plan; Main",
                "PDF",
                "Готово",
                "Экспорт выполнен.",
                @"C:\Exports\A-101.pdf")
        ]);

        Assert.Contains("SheetNumber;SheetName;Format;Status;Message;FilePath", csv);
        Assert.Contains("A-101;\"Plan; Main\";PDF;Готово;Экспорт выполнен.;C:\\Exports\\A-101.pdf", csv);
    }
}
