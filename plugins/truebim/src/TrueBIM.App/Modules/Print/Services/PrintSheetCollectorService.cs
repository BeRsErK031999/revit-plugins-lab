using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetCollectorService
{
    private const double MillimetersPerFoot = 304.8;

    public IReadOnlyList<PrintSheetInfo> Collect(Document document)
    {
        return Collect(document, CreateDefaultSourceId(document), ResolveSourceName(document), PrintSheetSourceKind.OpenDocument);
    }

    public IReadOnlyList<PrintSheetInfo> Collect(
        Document document,
        string sourceId,
        string sourceName)
    {
        return Collect(document, sourceId, sourceName, PrintSheetSourceKind.OpenDocument);
    }

    public IReadOnlyList<PrintSheetInfo> Collect(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(sourceId, nameof(sourceId));
        Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName));

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet =>
            {
                IReadOnlyDictionary<string, string> sheetParameters = CollectSheetParameters(sheet);
                return new PrintSheetInfo(
                    RevitElementIds.GetValue(sheet.Id),
                    sourceId,
                    sourceName,
                    sourceKind == PrintSheetSourceKind.LinkedDocument,
                    ResolveGroupName(sheetParameters),
                    sheet.SheetNumber,
                    sheet.Name,
                    ResolveSheetFormat(sheet),
                    sheet.IsPlaceholder,
                    !sheet.IsPlaceholder && sheet.CanBePrinted,
                    sheetParameters);
            })
            .OrderBy(sheet => sheet.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.SheetNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.SheetName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string CreateDefaultSourceId(Document document)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(document.PathName))
            {
                return document.PathName;
            }
        }
        catch (Exception)
        {
        }

        return ResolveSourceName(document);
    }

    private static string ResolveSourceName(Document document)
    {
        return string.IsNullOrWhiteSpace(document.Title)
            ? "Активный документ"
            : document.Title;
    }

    private static string ResolveSheetFormat(ViewSheet sheet)
    {
        if (sheet.IsPlaceholder)
        {
            return "Заглушка";
        }

        try
        {
            BoundingBoxUV outline = sheet.Outline;
            double width = Math.Abs(outline.Max.U - outline.Min.U) * MillimetersPerFoot;
            double height = Math.Abs(outline.Max.V - outline.Min.V) * MillimetersPerFoot;
            if (width <= 0 || height <= 0)
            {
                return "Не определен";
            }

            return $"{Math.Round(width)} x {Math.Round(height)} мм";
        }
        catch (Exception)
        {
            return "Не определен";
        }
    }

    private static IReadOnlyDictionary<string, string> CollectSheetParameters(ViewSheet sheet)
    {
        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (Parameter parameter in sheet.Parameters)
        {
            string name = parameter.Definition?.Name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name) || parameters.ContainsKey(name))
            {
                continue;
            }

            string value = parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters[name] = value.Trim();
            }
        }

        return parameters;
    }

    private static string ResolveGroupName(IReadOnlyDictionary<string, string> sheetParameters)
    {
        foreach (string parameterName in sheetParameters.Keys)
        {
            string normalizedName = parameterName.Trim().TrimStart('•', '-', ' ');
            if (string.Equals(normalizedName, "Том", StringComparison.CurrentCultureIgnoreCase))
            {
                return sheetParameters[parameterName];
            }
        }

        return "Без группы";
    }
}
