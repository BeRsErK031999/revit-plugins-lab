using TrueBIM.App.Modules.BimTools.BatchExport.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;

namespace TrueBIM.App.Modules.BimTools.BatchExport.Services;

public sealed class BatchExportReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IReadOnlyList<BatchExportReportRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        return csvExportService.Format(
            ["SheetNumber", "SheetName", "Format", "Status", "Message", "FilePath"],
            rows.Select(row => (IReadOnlyList<string?>)
            [
                row.SheetNumber,
                row.SheetName,
                row.Format,
                row.Status,
                row.Message,
                row.FilePath
            ]));
    }
}
