using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.TitleBlockFill.Models;

namespace TrueBIM.App.Modules.BimTools.TitleBlockFill.Services;

public sealed class TitleBlockFillReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IReadOnlyList<TitleBlockPreviewRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        return csvExportService.Format(
            ["SheetNumber", "SheetName", "Target", "ParameterName", "CurrentValue", "NewValue", "Status", "Message"],
            rows.Select(row => (IReadOnlyList<string?>)
            [
                row.SheetNumber,
                row.SheetName,
                row.Target,
                row.ParameterName,
                row.CurrentValue,
                row.NewValue,
                row.Status,
                row.Message
            ]));
    }
}
