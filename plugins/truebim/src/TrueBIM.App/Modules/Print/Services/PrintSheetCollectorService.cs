using Autodesk.Revit.DB;
using TrueBIM.App.Modules.Print.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.Print.Services;

public sealed class PrintSheetCollectorService
{
    public IReadOnlyList<PrintSheetInfo> Collect(Document document)
    {
        return Collect(
            document,
            CreateDefaultSourceId(document),
            ResolveSourceName(document),
            PrintSheetSourceKind.OpenDocument);
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
        return CollectCore(
            document,
            sourceId,
            sourceName,
            sourceKind,
            sheetParameterNames: null,
            titleBlockParameterNames: null).Sheets;
    }

    public IReadOnlyList<PrintSheetInfo> Collect(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind,
        IReadOnlyCollection<string> sheetParameterNames)
    {
        return Collect(
            document,
            sourceId,
            sourceName,
            sourceKind,
            sheetParameterNames,
            Array.Empty<string>());
    }

    public IReadOnlyList<PrintSheetInfo> Collect(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind,
        IReadOnlyCollection<string> sheetParameterNames,
        IReadOnlyCollection<string> titleBlockParameterNames)
    {
        return CollectWithCatalog(
            document,
            sourceId,
            sourceName,
            sourceKind,
            sheetParameterNames,
            titleBlockParameterNames).Sheets;
    }

    public PrintSheetCollection CollectWithCatalog(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind,
        IReadOnlyCollection<string> sheetParameterNames,
        IReadOnlyCollection<string> titleBlockParameterNames)
    {
        Guard.NotNull(sheetParameterNames, nameof(sheetParameterNames));
        Guard.NotNull(titleBlockParameterNames, nameof(titleBlockParameterNames));
        return CollectCore(
            document,
            sourceId,
            sourceName,
            sourceKind,
            sheetParameterNames,
            titleBlockParameterNames);
    }

    private static PrintSheetCollection CollectCore(
        Document document,
        string sourceId,
        string sourceName,
        PrintSheetSourceKind sourceKind,
        IReadOnlyCollection<string>? sheetParameterNames,
        IReadOnlyCollection<string>? titleBlockParameterNames)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNullOrWhiteSpace(sourceId, nameof(sourceId));
        Guard.NotNullOrWhiteSpace(sourceName, nameof(sourceName));

        HashSet<string> availableSheetParameterNames = new(StringComparer.CurrentCultureIgnoreCase);
        HashSet<string> availableTitleBlockParameterNames = new(StringComparer.CurrentCultureIgnoreCase);
        HashSet<long> catalogedTitleBlockTypeIds = [];
        List<PrintSheetInfo> sheets = [];
        IReadOnlyDictionary<long, FamilyInstance> titleBlocksBySheetId = CollectPrimaryTitleBlocks(document);

        foreach (ViewSheet sheet in new FilteredElementCollector(document)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>())
        {
            long sheetElementId = RevitElementIds.GetValue(sheet.Id);
            CollectParameterNames(sheet, availableSheetParameterNames);
            FamilyInstance? titleBlock = !sheet.IsPlaceholder
                && titleBlocksBySheetId.TryGetValue(sheetElementId, out FamilyInstance? primaryTitleBlock)
                    ? primaryTitleBlock
                    : null;
            CollectTitleBlockParameterNames(
                titleBlock,
                availableTitleBlockParameterNames,
                catalogedTitleBlockTypeIds);

            IReadOnlyDictionary<string, string> sheetParameters = CollectParameters(
                sheet,
                sheetParameterNames);
            IReadOnlyDictionary<string, string> titleBlockParameters = CollectTitleBlockParameters(
                titleBlock,
                titleBlockParameterNames);
            sheets.Add(new PrintSheetInfo(
                sheetElementId,
                sourceId,
                sourceName,
                sourceKind == PrintSheetSourceKind.LinkedDocument,
                ResolveGroupName(sheetParameters),
                sheet.SheetNumber,
                sheet.Name,
                ResolveSheetFormat(sheet, titleBlock),
                sheet.IsPlaceholder,
                !sheet.IsPlaceholder,
                sheetParameters,
                titleBlockParameters));
        }

        List<PrintSheetInfo> orderedSheets = sheets
            .OrderBy(sheet => sheet, PrintSheetComparer.Ascending)
            .ToList();
        PrintParameterCatalog catalog = new(
            OrderParameterNames(availableSheetParameterNames),
            OrderParameterNames(availableTitleBlockParameterNames),
            CollectProjectParameterNames(document));
        return new PrintSheetCollection(orderedSheets, catalog);
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

    private static IReadOnlyDictionary<long, FamilyInstance> CollectPrimaryTitleBlocks(Document document)
    {
        return new FilteredElementCollector(document)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .OfType<FamilyInstance>()
            .GroupBy(instance => RevitElementIds.GetValue(instance.OwnerViewId))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(
                    instance => instance.Name,
                    StringComparer.CurrentCultureIgnoreCase).First());
    }

    private static IReadOnlyDictionary<string, string> CollectParameters(
        Element element,
        IReadOnlyCollection<string>? requestedParameterNames)
    {
        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        if (requestedParameterNames is null)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                AddParameterValue(parameters, parameter);
            }

            return parameters;
        }

        if (element is ViewSheet sheet)
        {
            CollectGroupParameter(sheet, parameters);
        }

        foreach (string requestedParameterName in requestedParameterNames)
        {
            string parameterName = requestedParameterName.Trim();
            if (string.IsNullOrWhiteSpace(parameterName) || parameters.ContainsKey(parameterName))
            {
                continue;
            }

            Parameter? parameter = element.LookupParameter(parameterName);
            if (parameter is not null)
            {
                AddParameterValue(parameters, parameter);
            }
        }

        return parameters;
    }

    private static IReadOnlyDictionary<string, string> CollectTitleBlockParameters(
        FamilyInstance? titleBlock,
        IReadOnlyCollection<string>? requestedParameterNames)
    {
        if (titleBlock is null)
        {
            return new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        }

        Dictionary<string, string> parameters = new(StringComparer.CurrentCultureIgnoreCase);
        if (requestedParameterNames is null)
        {
            foreach (Parameter parameter in titleBlock.Parameters)
            {
                AddParameterValue(parameters, parameter);
            }

            foreach (Parameter parameter in titleBlock.Symbol.Parameters)
            {
                AddParameterValue(parameters, parameter);
            }

            return parameters;
        }

        foreach (string requestedParameterName in requestedParameterNames)
        {
            string parameterName = requestedParameterName.Trim();
            if (string.IsNullOrWhiteSpace(parameterName) || parameters.ContainsKey(parameterName))
            {
                continue;
            }

            Parameter? parameter = titleBlock.LookupParameter(parameterName)
                ?? titleBlock.Symbol.LookupParameter(parameterName);
            if (parameter is not null)
            {
                AddParameterValue(parameters, parameter);
            }
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

    private static void CollectParameterNames(Element element, HashSet<string> names)
    {
        foreach (Parameter parameter in element.Parameters)
        {
            string name = parameter.Definition?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }
    }

    private static void CollectTitleBlockParameterNames(
        FamilyInstance? titleBlock,
        HashSet<string> names,
        HashSet<long> catalogedTypeIds)
    {
        if (titleBlock is null)
        {
            return;
        }

        long typeId = RevitElementIds.GetValue(titleBlock.GetTypeId());
        if (!catalogedTypeIds.Add(typeId))
        {
            return;
        }

        CollectParameterNames(titleBlock, names);
        CollectParameterNames(titleBlock.Symbol, names);
    }

    private static IReadOnlyList<string> CollectProjectParameterNames(Document document)
    {
        if (document.ProjectInformation is null)
        {
            return Array.Empty<string>();
        }

        HashSet<string> names = new(StringComparer.CurrentCultureIgnoreCase);
        CollectParameterNames(document.ProjectInformation, names);
        return OrderParameterNames(names);
    }

    private static IReadOnlyList<string> OrderParameterNames(IEnumerable<string> names)
    {
        return names
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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

    private static string ResolveSheetFormat(ViewSheet sheet, FamilyInstance? titleBlock)
    {
        if (sheet.IsPlaceholder)
        {
            return "Заглушка";
        }

        if (titleBlock is null)
        {
            return "Без основной надписи";
        }

        double? width = GetTitleBlockDimension(titleBlock, BuiltInParameter.SHEET_WIDTH);
        double? height = GetTitleBlockDimension(titleBlock, BuiltInParameter.SHEET_HEIGHT);
        if (width is > 0 && height is > 0)
        {
            double widthMillimeters = ConvertLengthFromInternalUnits(width.Value);
            double heightMillimeters = ConvertLengthFromInternalUnits(height.Value);
            return $"{Math.Round(widthMillimeters)} x {Math.Round(heightMillimeters)} мм";
        }

        string familyName = titleBlock.Symbol.FamilyName;
        string typeName = titleBlock.Name;
        return string.IsNullOrWhiteSpace(familyName)
            ? typeName
            : $"{familyName}: {typeName}";
    }

    private static double ConvertLengthFromInternalUnits(double internalLength)
    {
#if REVIT2022_OR_GREATER
        return UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Millimeters);
#else
        return UnitUtils.ConvertFromInternalUnits(internalLength, DisplayUnitType.DUT_MILLIMETERS);
#endif
    }

    private static double? GetTitleBlockDimension(
        FamilyInstance titleBlock,
        BuiltInParameter builtInParameter)
    {
        Parameter? parameter = titleBlock.get_Parameter(builtInParameter)
            ?? titleBlock.Symbol.get_Parameter(builtInParameter);
        return parameter?.StorageType == StorageType.Double
            ? parameter.AsDouble()
            : null;
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
