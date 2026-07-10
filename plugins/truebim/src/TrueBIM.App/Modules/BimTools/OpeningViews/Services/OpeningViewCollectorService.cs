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
            message = "Фасады проёмов работают только на обычном активном плане, а не на шаблоне вида.";
            return false;
        }

        if (activeView is not ViewPlan)
        {
            message = "Откройте активный план, на котором видны двери, окна или витражи.";
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
            message = "Откройте активный план, на котором видны двери, окна или витражи.";
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

        if (profile.IncludeCurtainWalls)
        {
            candidates.AddRange(new FilteredElementCollector(document, activePlan.Id)
                .OfClass(typeof(Wall))
                .WhereElementIsNotElementType()
                .Cast<Wall>()
                .Where(OpeningViewElementClassifier.IsCurtainWall)
                .Select(wall => CreateCurtainWallCandidate(document, activePlan, wall, profile, existingViewNames)));
        }

        return candidates
            .OrderBy(candidate => candidate.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.LevelName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.TypeName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(candidate => candidate.ElementId)
            .ToList();
    }

    private static OpeningViewCandidate CreateCurtainWallCandidate(
        Document document,
        ViewPlan activePlan,
        Wall wall,
        OpeningViewProfile profile,
        HashSet<string> existingViewNames)
    {
        long elementId = RevitElementIds.GetValue(wall.Id);
        WallType wallType = wall.WallType;
        string fallbackName = string.IsNullOrWhiteSpace(wall.Name)
            ? elementId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : wall.Name;
        string familyName = string.IsNullOrWhiteSpace(wallType.FamilyName) ? "Витраж" : wallType.FamilyName;
        string typeName = string.IsNullOrWhiteSpace(wallType.Name) ? fallbackName : wallType.Name;
        string levelName = GetLevelName(document, wall);
        string viewName = OpeningViewNameService.Build(
            profile.ViewNameTemplate,
            new OpeningViewNameContext(
                elementId,
                OpeningViewCategoryKeys.CurtainWall,
                "Витраж",
                familyName,
                typeName,
                levelName));

        OpeningViewBoundsResult? boundsResult = OpeningViewBoundsResolver.Resolve(wall, activePlan);
        OpeningViewBounds? bounds = boundsResult?.Bounds;
        XYZ origin = GetOrigin(wall, bounds);
        bool isStraight = TryResolveCurtainWallDirection(wall, activePlan.UpDirection, out XYZ direction);
        string message = isStraight
            ? "Готово к созданию elevation-вида. Ориентация: по наружной стороне прямолинейного витража."
            : "Дуговой или вырожденный витраж нельзя представить одним плоским фасадом; для него нужна развёртка.";

        if (boundsResult?.UsedViewSpecificFallback == true)
        {
            message += " Полная модельная геометрия недоступна: для crop используется резервный bounding box активного плана.";
        }

        bool canApply = isStraight;
        if (profile.ElevationViewTypeId is null)
        {
            canApply = false;
            message = "Выберите тип фасадного вида.";
        }
        else if (bounds is null)
        {
            canApply = false;
            message = "Не найден bounding box полной или видовой геометрии витража.";
        }
        else if (existingViewNames.Contains(viewName))
        {
            canApply = false;
            message = "Вид с таким именем уже существует.";
        }

        return new OpeningViewCandidate(
            elementId,
            OpeningViewCategoryKeys.CurtainWall,
            "Витраж",
            familyName,
            typeName,
            levelName,
            viewName,
            origin,
            direction,
            OpeningViewOrientationSources.HostWall,
            orientationFallback: false,
            bounds is null ? XYZ.Zero : new XYZ(bounds.MinX, bounds.MinY, bounds.MinZ),
            bounds is null ? XYZ.Zero : new XYZ(bounds.MaxX, bounds.MaxY, bounds.MaxZ),
            profile.ElevationViewTypeId,
            profile.ViewTemplateId,
            message,
            canApply);
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

        OpeningViewBoundsResult? boundsResult = OpeningViewBoundsResolver.Resolve(familyInstance, activePlan);
        OpeningViewBounds? bounds = boundsResult?.Bounds;
        XYZ origin = GetOrigin(familyInstance, bounds);
        OpeningViewOrientationResult orientation = OpeningViewOrientationResolver.Resolve(familyInstance, activePlan, profile);

        string message = $"Готово к созданию elevation-вида. Ориентация: {OpeningViewOrientationSources.GetDisplayName(orientation.Source).ToLowerInvariant()}.";
        if (orientation.UsedFallback)
        {
            message += " Стена-основа не найдена, используется ориентация элемента.";
        }

        if (boundsResult?.UsedViewSpecificFallback == true)
        {
            message += " Полная модельная геометрия недоступна: для crop используется резервный bounding box активного плана.";
        }

        bool canApply = true;
        if (profile.ElevationViewTypeId is null)
        {
            canApply = false;
            message = "Выберите тип фасадного вида.";
        }
        else if (bounds is null)
        {
            canApply = false;
            message = "Не найден bounding box полной или видовой геометрии элемента.";
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
            bounds is null ? XYZ.Zero : new XYZ(bounds.MinX, bounds.MinY, bounds.MinZ),
            bounds is null ? XYZ.Zero : new XYZ(bounds.MaxX, bounds.MaxY, bounds.MaxZ),
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

    private static XYZ GetOrigin(Element element, OpeningViewBounds? bounds)
    {
        if (element.Location is LocationPoint locationPoint)
        {
            return locationPoint.Point;
        }

        if (element.Location is LocationCurve locationCurve && locationCurve.Curve is Line line)
        {
            return (line.GetEndPoint(0) + line.GetEndPoint(1)).Multiply(0.5);
        }

        return bounds is null
            ? XYZ.Zero
            : new XYZ(
                (bounds.MinX + bounds.MaxX) * 0.5,
                (bounds.MinY + bounds.MaxY) * 0.5,
                (bounds.MinZ + bounds.MaxZ) * 0.5);
    }

    private static string GetLevelName(Document document, Element element)
    {
        ElementId levelId = element.LevelId;
        Element? level = levelId == ElementId.InvalidElementId ? null : document.GetElement(levelId);
        string? levelName = level?.Name;
        return string.IsNullOrWhiteSpace(levelName) ? "Без уровня" : levelName!;
    }

    private static bool TryResolveCurtainWallDirection(Wall wall, XYZ fallback, out XYZ direction)
    {
        direction = NormalizeHorizontal(fallback, XYZ.BasisY);
        if (wall.Location is not LocationCurve locationCurve || locationCurve.Curve is not Line wallLine)
        {
            return false;
        }

        XYZ start = wallLine.GetEndPoint(0);
        XYZ end = wallLine.GetEndPoint(1);
        XYZ wallFacing = NormalizeHorizontal(wall.Orientation, fallback);
        if (!OpeningViewOrientationResolver.TryResolveWallFacingCoordinates(
            start.X,
            start.Y,
            end.X,
            end.Y,
            wallFacing.X,
            wallFacing.Y,
            out double x,
            out double y))
        {
            return false;
        }

        direction = new XYZ(x, y, 0);
        return true;
    }

    private static XYZ NormalizeHorizontal(XYZ value, XYZ fallback)
    {
        XYZ horizontal = new(value.X, value.Y, 0);
        if (horizontal.GetLength() < 1e-6)
        {
            horizontal = new(fallback.X, fallback.Y, 0);
        }

        return horizontal.GetLength() < 1e-6 ? XYZ.BasisY : horizontal.Normalize();
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
