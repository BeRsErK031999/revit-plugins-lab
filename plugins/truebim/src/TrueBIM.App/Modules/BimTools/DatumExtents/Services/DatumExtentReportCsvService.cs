using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.DatumExtents.Models;

namespace TrueBIM.App.Modules.BimTools.DatumExtents.Services;

public sealed class DatumExtentReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IReadOnlyList<DatumExtentReportRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        return csvExportService.Format(
            ["Phase", "ViewName", "ElementId", "Kind", "Name", "TargetExtentType", "End0Type", "End1Type", "Status", "Message"],
            rows.Select(row => (IReadOnlyList<string?>)
            [
                row.Phase,
                row.ViewName,
                row.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Kind,
                row.Name,
                row.TargetExtentType,
                row.End0Type,
                row.End1Type,
                row.Status,
                row.Message
            ]));
    }
}
