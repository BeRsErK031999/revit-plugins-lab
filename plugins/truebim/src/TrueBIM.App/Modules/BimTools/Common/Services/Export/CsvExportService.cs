using System.IO;
using System.Text;

namespace TrueBIM.App.Modules.BimTools.Common.Services.Export;

public sealed class CsvExportService
{
    private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public string Format(
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows,
        char delimiter = ';')
    {
        Guard.NotNull(headers, nameof(headers));
        Guard.NotNull(rows, nameof(rows));

        StringBuilder builder = new();
        AppendRow(builder, headers.Select(header => (string?)header), delimiter);

        foreach (IReadOnlyList<string?> row in rows)
        {
            AppendRow(builder, row, delimiter);
        }

        return builder.ToString();
    }

    public void WriteUtf8WithBom(string path, string csv)
    {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        Guard.NotNull(csv, nameof(csv));

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, csv, Utf8WithBom);
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string?> values, char delimiter)
    {
        bool isFirst = true;
        foreach (string? value in values)
        {
            if (!isFirst)
            {
                builder.Append(delimiter);
            }

            builder.Append(Escape(value ?? string.Empty, delimiter));
            isFirst = false;
        }

        builder.AppendLine();
    }

    private static string Escape(string value, char delimiter)
    {
        bool shouldQuote = value.Contains(delimiter)
            || value.Contains('"')
            || value.Contains('\r')
            || value.Contains('\n');

        if (!shouldQuote)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
