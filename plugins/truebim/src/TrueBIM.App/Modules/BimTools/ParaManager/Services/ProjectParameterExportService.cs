using System.Text;
using TrueBIM.App.Modules.BimTools.ParaManager.Models;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class ProjectParameterExportService
{
    private static readonly string[] Headers =
    [
        "ParameterName",
        "BindingType",
        "Categories",
        "GroupUnder",
        "DataType",
        "IsShared",
        "Guid"
    ];

    public string BuildCsv(IReadOnlyList<ProjectParameterRow> rows)
    {
        StringBuilder builder = new();
        builder.AppendLine(string.Join(";", Headers));

        foreach (ProjectParameterRow row in rows)
        {
            builder.AppendLine(string.Join(";", [
                Escape(row.Name),
                Escape(row.BindingTypeDisplay),
                Escape(row.CategoriesDisplay),
                Escape(row.GroupDisplay),
                Escape(row.DataTypeDisplay),
                row.IsShared ? "true" : "false",
                Escape(row.GuidDisplay)
            ]));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        string safeValue = value ?? string.Empty;
        bool mustQuote = safeValue.IndexOfAny([';', '"', '\r', '\n']) >= 0;
        if (!mustQuote)
        {
            return safeValue;
        }

        return $"\"{safeValue.Replace("\"", "\"\"")}\"";
    }
}
