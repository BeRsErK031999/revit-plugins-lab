using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Services;

public sealed class AutoTagReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IReadOnlyList<AutoTagReportRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        return csvExportService.Format(
            ["Phase", "ViewName", "ElementId", "CategoryName", "ElementName", "TagType", "Status", "Message"],
            rows.Select(row => (IReadOnlyList<string?>)
            [
                row.Phase,
                row.ViewName,
                row.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.CategoryName,
                row.ElementName,
                row.TagTypeName,
                row.Status,
                row.Message
            ]));
    }
}
