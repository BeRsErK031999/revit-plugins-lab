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
                "Source",
                "ClashId",
                "Fingerprint",
                "ClashName",
                "Type",
                "Priority",
                "SeverityScore",
                "Group",
                "AssignedTo",
                "Element1Source",
                "ElementId1",
                "Element1Resolved",
                "Element1Name",
                "Element2Source",
                "ElementId2",
                "Element2Resolved",
                "Element2Name",
                "X",
                "Y",
                "Z",
                "ApproximateVolumeMm3",
                "Status",
                "Comment",
                "Message"
            ],
            items.Select(item => (IReadOnlyList<string?>)
            [
                item.Source,
                item.ClashId,
                item.Fingerprint,
                item.Name,
                item.ClashTypeDisplay,
                item.PriorityDisplay,
                item.SeverityDisplay,
                item.GroupKey,
                item.AssignedTo,
                item.Element1SourceName,
                item.ElementId1Text,
                item.IsElement1Resolved ? "yes" : "no",
                item.Element1Name,
                item.Element2SourceName,
                item.ElementId2Text,
                item.IsElement2Resolved ? "yes" : "no",
                item.Element2Name,
                FormatDouble(item.X),
                FormatDouble(item.Y),
                FormatDouble(item.Z),
                FormatDouble(item.ApproximateVolumeMm3),
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
