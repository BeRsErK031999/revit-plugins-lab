using Autodesk.Revit.DB;
using TrueBIM.App.Modules.SheetNumbering.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.SheetNumbering.Services;

public sealed class SheetCollectorService
{
    public IReadOnlyList<SheetInfo> Collect(Document document)
    {
        Guard.NotNull(document, nameof(document));

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(sheet =>
            {
                IReadOnlyDictionary<string, string> sheetParameters = CollectSheetParameters(sheet);
                return new SheetInfo(
                    RevitElementIds.GetValue(sheet.Id),
                    sheet.SheetNumber,
                    sheet.Name,
                    sheet.IsPlaceholder,
                    ResolveGroupName(sheetParameters));
            })
            .OrderBy(sheet => sheet.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.CurrentNumber, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(sheet => sheet.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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
        foreach (var parameter in sheetParameters)
        {
            string normalizedName = parameter.Key.Trim().TrimStart('•', '-', ' ');
            if (IsGroupParameterName(normalizedName))
            {
                return parameter.Value;
            }
        }

        return "Без группы";
    }

    private static bool IsGroupParameterName(string parameterName)
    {
        return string.Equals(parameterName, "Раздел проекта", StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(parameterName, "Том", StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(parameterName, "Раздел", StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(parameterName, "Группа листов", StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(parameterName, "Группа", StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(parameterName, "Sheet Group", StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(parameterName, "SheetGroup", StringComparison.CurrentCultureIgnoreCase);
    }
}
