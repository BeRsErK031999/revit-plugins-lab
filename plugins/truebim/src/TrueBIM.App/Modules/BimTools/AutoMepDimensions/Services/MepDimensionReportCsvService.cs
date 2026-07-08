using TrueBIM.App.Modules.BimTools.AutoMepDimensions.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public sealed class MepDimensionReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IReadOnlyList<MepDimensionReportRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        return csvExportService.Format(
            ["Phase", "ViewName", "CandidateId", "CategoryName", "DirectionName", "ElementCount", "ReadyReferenceCount", "MissingReferenceCount", "Status", "Message"],
            rows.Select(row => (IReadOnlyList<string?>)
            [
                row.Phase,
                row.ViewName,
                row.CandidateId,
                row.CategoryName,
                row.DirectionName,
                row.ElementCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.ReadyReferenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.MissingReferenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                row.Status,
                row.Message
            ]));
    }
}
