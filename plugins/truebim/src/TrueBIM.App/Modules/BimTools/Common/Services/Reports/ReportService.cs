using TrueBIM.App.Modules.BimTools.Common.Models;
using TrueBIM.App.Modules.BimTools.Common.Services.Export;

namespace TrueBIM.App.Modules.BimTools.Common.Services.Reports;

public sealed class ReportService
{
    public BimReport CreateScaffoldReport(string toolTitle, string documentTitle, string actionName)
    {
        Guard.NotNullOrWhiteSpace(toolTitle, nameof(toolTitle));
        Guard.NotNullOrWhiteSpace(documentTitle, nameof(documentTitle));
        Guard.NotNullOrWhiteSpace(actionName, nameof(actionName));

        return new BimReport(
            toolTitle,
            DateTimeOffset.UtcNow,
            [
                new BimReportEntry(
                    actionName,
                    "Каркас",
                    $"Команда '{toolTitle}' открыта для документа '{documentTitle}'. Изменения модели в этом срезе не выполняются.")
            ]);
    }

    public string FormatCsv(BimReport report, CsvExportService csvExportService)
    {
        Guard.NotNull(report, nameof(report));
        Guard.NotNull(csvExportService, nameof(csvExportService));

        return csvExportService.Format(
            ["CreatedAtUtc", "Title", "Scope", "Status", "Message"],
            report.Entries.Select(entry => (IReadOnlyList<string?>)
            [
                report.CreatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                report.Title,
                entry.Scope,
                entry.Status,
                entry.Message
            ]));
    }
}
