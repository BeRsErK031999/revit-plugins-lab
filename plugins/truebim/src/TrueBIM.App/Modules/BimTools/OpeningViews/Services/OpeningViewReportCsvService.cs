using TrueBIM.App.Modules.BimTools.Common.Services.Export;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IReadOnlyList<OpeningViewReportRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        return csvExportService.Format(
            ["Phase", "SourceViewName", "ElementId", "CategoryName", "FamilyName", "TypeName", "LevelName", "ViewName", "Status", "Message"],
            rows.Select(row => (IReadOnlyList<string?>)
            [
                row.Phase,
                row.SourceViewName,
                row.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.CategoryName,
                row.FamilyName,
                row.TypeName,
                row.LevelName,
                row.ViewName,
                row.Status,
                row.Message
            ]));
    }
}
