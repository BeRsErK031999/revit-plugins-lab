using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.OpeningViews.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.OpeningViews.Services;

public sealed class OpeningViewCollectorService
{
    private static readonly IReadOnlyList<CategoryDefinition> CategoryDefinitions =
    [
        new(OpeningViewCategoryKeys.Door, "Дверь", BuiltInCategory.OST_Doors),
        new(OpeningViewCategoryKeys.Window, "Окно", BuiltInCategory.OST_Windows)
    ];

    public static bool CanUseActiveView(View? activeView, out string message)
    {
        if (activeView is null)
        {
            message = "Активный вид не найден.";
            return false;
        }

        if (activeView.IsTemplate)
        {
            message = "Фасады дверей/окон работают только на обычном активном плане, а не на шаблоне вида.";
            return false;
        }

        if (activeView is not ViewPlan)
        {
            message = "Откройте активный план, на котором видны двери или окна.";
            return false;
        }

        if (activeView.ViewType is ViewType.DrawingSheet
            or ViewType.ProjectBrowser
            or ViewType.SystemBrowser
            or ViewType.Internal
            or ViewType.Schedule
            or ViewType.Report
            or ViewType.Legend
            or ViewType.ThreeD)
        {
            message = "Откройте активный план, на котором видны двери или окна.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public IReadOnlyList<OpeningViewTypeOption> CollectElevationViewTypes(Document document)
    {
        Guard.NotNull(document, nameof(document));

        return new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .Where(type => type.ViewFamily == ViewFamily.Elevation)
            .OrderBy(type => type.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(type => new OpeningViewTypeOption(RevitElementIds.GetValue(type.Id), type.Name))
            .ToList();
    }

    public IReadOnlyList<OpeningViewTemplateOption> CollectViewTemplates(Document document)
    {
        Guard.NotNull(document, nameof(document));

        List<OpeningViewTemplateOption> options = [OpeningViewTemplateOption.None];
        options.AddRange(new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => view.IsTemplate && view.ViewType == ViewType.Elevation)
            .OrderBy(view => view.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(view => new OpeningViewTemplateOption(RevitElementIds.GetValue(view.Id), view.Name)));

        return options;
    }

    public IReadOnlyList<OpeningViewCandidate> CollectOpenings(
        Document document,
        ViewPlan activePlan,
        OpeningViewProfile profile)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activePlan, nameof(activePlan));
        Guard.NotNull(profile, nameof(profile));

        HashSet<string> existingViewNames = CollectExistingViewNames(document);
        List<OpeningViewCandidate> candidates = [];
        foreach (CategoryDefinition category in GetEnabledCategories(profile))
        {
            foreach (Element element in new FilteredElementCollector(document, activePlan.Id)
                .OfCategory(category.BuiltInCategory)
                .WhereElementIsNotElementType())
            {
                if (element is FamilyInstance familyInstance)
                {
                    candidates.Add(CreateCandidate(document, activePlan, familyInstance, category, profile, existingViewNames));
                }
            }
        }

        return candidates
            .OrderBy(candidate => candidate.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.LevelName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.ElementId)
            .ToList();
    }

    private static OpeningViewCandidate CreateCandidate(
        Document document,
        ViewPlan activePlan,
        FamilyInstance familyInstance,
        CategoryDefinition category,
        OpeningViewProfile profile,
        HashSet<string> existingViewNames)
    {
        long elementId = RevitElementIds.GetValue(familyInstance.Id);
        FamilySymbol? symbol = familyInstance.Symbol;
        string fallbackName = string.IsNullOrWhiteSpace(familyInstance.Name) ? elementId.ToString(System.Globalization.CultureInfo.InvariantCulture) : familyInstance.Name;
        string? symbolFamilyName = symbol?.FamilyName;
        string? symbolTypeName = symbol?.Name;
        string familyName = string.IsNullOrWhiteSpace(symbolFamilyName) ? fallbackName : symbolFamilyName!;
        string typeName = string.IsNullOrWhiteSpace(symbolTypeName) ? fallbackName : symbolTypeName!;
        string levelName = GetLevelName(document, familyInstance);
        string viewName = OpeningViewNameService.Build(
            profile.ViewNameTemplate,
            new OpeningViewNameContext(
                elementId,
                category.Key,
                category.DisplayName,
                familyName,
                typeName,
                levelName));

        BoundingBoxXYZ? boundingBox = familyInstance.get_BoundingBox(activePlan) ?? familyInstance.get_BoundingBox(null);
        XYZ origin = GetOrigin(familyInstance, boundingBox);
        OpeningViewOrientationResult orientation = OpeningViewOrientationResolver.Resolve(familyInstance, activePlan, profile);

        string message = $"Готово к созданию elevation-вида. Ориентация: {OpeningViewOrientationSources.GetDisplayName(orientation.Source).ToLowerInvariant()}.";
        if (orientation.UsedFallback)
        {
            message += " Стена-основа не найдена, используется ориентация элемента.";
        }

        bool canApply = true;
        if (profile.ElevationViewTypeId is null)
        {
            canApply = false;
            message = "Выберите тип фасадного вида.";
        }
        else if (boundingBox is null)
        {
            canApply = false;
            message = "Не найден bounding box элемента на активном плане.";
        }
        else if (existingViewNames.Contains(viewName))
        {
            canApply = false;
            message = "Вид с таким именем уже существует.";
        }

        return new OpeningViewCandidate(
            elementId,
            category.Key,
            category.DisplayName,
            familyName,
            typeName,
            levelName,
            viewName,
            origin,
            orientation.Direction,
            orientation.Source,
            orientation.UsedFallback,
            boundingBox?.Min ?? XYZ.Zero,
            boundingBox?.Max ?? XYZ.Zero,
            profile.ElevationViewTypeId,
            profile.ViewTemplateId,
            message,
            canApply);
    }

    private static HashSet<string> CollectExistingViewNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(view => !view.IsTemplate)
            .Select(view => view.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
    }

    private static XYZ GetOrigin(FamilyInstance familyInstance, BoundingBoxXYZ? boundingBox)
    {
        if (familyInstance.Location is LocationPoint locationPoint)
        {
            return locationPoint.Point;
        }

        return boundingBox is null
            ? XYZ.Zero
            : (boundingBox.Min + boundingBox.Max).Multiply(0.5);
    }

    private static string GetLevelName(Document document, FamilyInstance familyInstance)
    {
        ElementId levelId = familyInstance.LevelId;
        Element? level = levelId == ElementId.InvalidElementId ? null : document.GetElement(levelId);
        string? levelName = level?.Name;
        return string.IsNullOrWhiteSpace(levelName) ? "Без уровня" : levelName!;
    }

    private static IEnumerable<CategoryDefinition> GetEnabledCategories(OpeningViewProfile profile)
    {
        foreach (CategoryDefinition category in CategoryDefinitions)
        {
            if (category.Key == OpeningViewCategoryKeys.Door && profile.IncludeDoors
                || category.Key == OpeningViewCategoryKeys.Window && profile.IncludeWindows)
            {
                yield return category;
            }
        }
    }

    private sealed record CategoryDefinition(string Key, string DisplayName, BuiltInCategory BuiltInCategory);
}
