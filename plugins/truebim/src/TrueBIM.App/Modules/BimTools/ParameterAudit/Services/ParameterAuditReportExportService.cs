using System.Globalization;
using System.IO;
using System.Text;
using TrueBIM.App.Modules.BimTools.ParameterAudit.Models;

namespace TrueBIM.App.Modules.BimTools.ParameterAudit.Services;

public sealed class ParameterAuditReportExportService
{
    public void ExportCsv(string path, ParameterAuditReport report)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Путь к отчёту не указан.", nameof(path));
        }

        StringBuilder builder = new();
        AppendRow(builder,
        [
            "Severity",
            "RuleId",
            "IssueCode",
            "SourceModel",
            "IsLinked",
            "ElementId",
            "Category",
            "Family",
            "Type",
            "Parameter",
            "ActualValue",
            "ExpectedValue",
            "Message"
        ]);
        foreach (ParameterAuditResultRow row in report.Rows)
        {
            AppendRow(builder,
            [
                row.SeverityDisplay,
                row.RuleId,
                row.IssueCode.ToString(),
                row.SourceModel,
                row.IsLinked ? "true" : "false",
                row.ElementId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.CategoryName,
                row.FamilyName,
                row.TypeName,
                row.ParameterName,
                row.ActualValue,
                row.ExpectedValue,
                row.Message
            ]);
        }

        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string> cells)
    {
        builder.AppendLine(string.Join(";", cells.Select(Escape)));
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([';', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
