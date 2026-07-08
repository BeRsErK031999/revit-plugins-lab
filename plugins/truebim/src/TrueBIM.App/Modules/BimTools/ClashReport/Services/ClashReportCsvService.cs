using System.Globalization;
using TrueBIM.App.Modules.BimTools.ClashReport.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;

namespace TrueBIM.App.Modules.BimTools.ClashReport.Services;

public sealed class ClashReportCsvService
{
    private readonly CsvExportService csvExportService = new();

    public string Format(IEnumerable<ClashItem> items)
    {
        Guard.NotNull(items, nameof(items));

        return csvExportService.Format(
            [
                "ClashId",
                "ClashName",
                "ElementId1",
                "Element1Resolved",
                "Element1Name",
                "ElementId2",
                "Element2Resolved",
                "Element2Name",
                "X",
                "Y",
                "Z",
                "Status",
                "Comment",
                "Message"
            ],
            items.Select(item => (IReadOnlyList<string?>)
            [
                item.ClashId,
                item.Name,
                item.ElementId1Text,
                item.IsElement1Resolved ? "yes" : "no",
                item.Element1Name,
                item.ElementId2Text,
                item.IsElement2Resolved ? "yes" : "no",
                item.Element2Name,
                FormatDouble(item.X),
                FormatDouble(item.Y),
                FormatDouble(item.Z),
                ClashStatuses.ToDisplayName(item.Status),
                item.Comment,
                item.Message
            ]));
    }

    private static string FormatDouble(double? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
