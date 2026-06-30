using System.Text;
using TrueBIM.App.Modules.SheetNumbering.Models;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetNumberPreviewExportFormatter
{
    public string FormatCsv(IReadOnlyList<SheetNumberPreviewExportRow> rows)
    {
        Guard.NotNull(rows, nameof(rows));

        StringBuilder builder = new();
        builder.AppendLine("ElementId,CurrentNumber,NewNumber,SheetName,IsPlaceholder,Status");

        foreach (SheetNumberPreviewExportRow row in rows)
        {
            builder
                .Append(row.ElementId)
                .Append(',')
                .Append(Escape(row.CurrentNumber))
                .Append(',')
                .Append(Escape(row.NewNumber))
                .Append(',')
                .Append(Escape(row.SheetName))
                .Append(',')
                .Append(row.IsPlaceholder ? "true" : "false")
                .Append(',')
                .AppendLine(Escape(row.Status));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
