using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetCollectorService
{
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
        return CollectCore(document, sourceId, sourceName, sourceKind, sheetParameterNames: null);
    }

    public IReadOnlyList<PrintSheetInfo> Collect(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind,
        IReadOnlyCollection<string> sheetParameterNames)
    {
        Guard.NotNull(sheetParameterNames, nameof(sheetParameterNames));
        return CollectCore(document, sourceId, sourceName, sourceKind, sheetParameterNames);
    }

    private static IReadOnlyList<PrintSheetInfo> CollectCore(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind,
        IReadOnlyCollection<string>? sheetParameterNames)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(sourceId, nameof(sourceId));
        Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName));

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet =>
            {
                IReadOnlyDictionary<string, string> sheetParameters = CollectSheetParameters(sheet, sheetParameterNames);
                return new PrintSheetInfo(
                    RevitElementIds.GetValue(sheet.Id),
                    sourceId,
                    sourceName,
                    sourceKind == PrintSheetSourceKind.LinkedDocument,
                    ResolveGroupName(sheetParameters),
                    sheet.SheetNumber,
                    sheet.Name,
                    sheet.IsPlaceholder ? "Заглушка" : "—",
                    sheet.IsPlaceholder,
                    !sheet.IsPlaceholder,
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

    private static IReadOnlyDictionary<string, string> CollectSheetParameters(
        ViewSheet sheet,
        IReadOnlyCollection<string>? requestedParameterNames)
    {
        if (requestedParameterNames is null)
        {
            return CollectAllSheetParameters(sheet);
        }

        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        CollectGroupParameter(sheet, parameters);
        foreach (string requestedParameterName in requestedParameterNames)
        {
            string parameterName = requestedParameterName.Trim();
            if (string.IsNullOrWhiteSpace(parameterName) || parameters.ContainsKey(parameterName))
            {
                continue;
            }

            Parameter? parameter = sheet.LookupParameter(parameterName);
            if (parameter is not null)
            {
                AddParameterValue(parameters, parameter);
            }
        }

        return parameters;
    }

    private static IReadOnlyDictionary<string, string> CollectAllSheetParameters(ViewSheet sheet)
    {
        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        foreach (Parameter parameter in sheet.Parameters)
        {
            AddParameterValue(parameters, parameter);
        }

        return parameters;
    }

    private static void CollectGroupParameter(
        ViewSheet sheet,
        Dictionary<string, string> parameters)
    {
        foreach (Parameter parameter in sheet.Parameters)
        {
            string name = parameter.Definition?.Name ?? string.Empty;
            if (!IsGroupParameterName(name))
            {
                continue;
            }

            AddParameterValue(parameters, parameter);
            break;
        }
    }

    private static void AddParameterValue(
        Dictionary<string, string> parameters,
        Parameter parameter)
    {
        string name = parameter.Definition?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || parameters.ContainsKey(name))
        {
            return;
        }

        string value = parameter.AsValueString() ?? parameter.AsString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[name] = value.Trim();
        }
    }

    private static string ResolveGroupName(IReadOnlyDictionary<string, string> sheetParameters)
    {
        foreach (string parameterName in sheetParameters.Keys)
        {
            if (IsGroupParameterName(parameterName))
            {
                return sheetParameters[parameterName];
            }
        }

        return "Без группы";
    }

    private static bool IsGroupParameterName(string parameterName)
    {
        string normalizedName = parameterName.Trim().TrimStart('•', '-', ' ');
        return string.Equals(normalizedName, "Том", StringComparison.CurrentCultureIgnoreCase);
    }
}
