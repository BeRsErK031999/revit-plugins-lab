using Autodesk.Revit.DB;
using TrueBIM.App.Modules.BimTools.AutoTags.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.AutoTags.Services;

public sealed class AutoTagCollectorService
{
    private readonly AutoTagPlacementService placementService;

    public AutoTagCollectorService(AutoTagPlacementService placementService)
    {
        this.placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
    }

    public static bool CanUseActiveView(View? activeView, out string message)
    {
        if (activeView is null)
        {
            message = "Активный вид не найден.";
            return false;
        }

        if (activeView.IsTemplate)
        {
            message = "Автомарки работают только на обычном активном виде, а не на шаблоне вида.";
            return false;
        }

        if (activeView.ViewType is ViewType.ThreeD
            or ViewType.DrawingSheet
            or ViewType.ProjectBrowser
            or ViewType.SystemBrowser
            or ViewType.Internal)
        {
            message = "Откройте 2D-вид модели: план, разрез, фасад, потолочный план или чертёжный вид.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public IReadOnlyList<AutoTagCategoryOption> CollectCategories(Document document, View activeView)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));

        Dictionary<long, CategoryBucket> buckets = [];
        foreach (Element element in CollectVisibleElements(document, activeView))
        {
            Category? category = element.Category;
            if (!CanUseCategory(category))
            {
                continue;
            }

            long categoryId = RevitElementIds.GetValue(category.Id);
            if (!buckets.TryGetValue(categoryId, out CategoryBucket? bucket))
            {
                bucket = new CategoryBucket(category.Id, category.Name);
                buckets.Add(categoryId, bucket);
            }

            bucket.ElementCount++;
        }

        return buckets.Values
            .OrderBy(bucket => bucket.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(bucket => new AutoTagCategoryOption(bucket.CategoryId, bucket.Name, bucket.ElementCount))
            .ToList();
    }

    public IReadOnlyList<AutoTagTypeOption> CollectTagTypes(Document document)
    {
        Guard.NotNull(document, nameof(document));

        List<AutoTagTypeOption> types = new()
        {
            AutoTagTypeOption.Automatic
        };

        IEnumerable<FamilySymbol> symbols = new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(symbol => IsTagCategory(symbol.Category));

        types.AddRange(symbols
            .OrderBy(symbol => symbol.FamilyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(symbol => symbol.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(symbol => new AutoTagTypeOption(
                RevitElementIds.GetValue(symbol.Id),
                symbol.FamilyName,
                symbol.Name,
                symbol.Category?.Name ?? string.Empty)));

        return types;
    }

    public IReadOnlyList<AutoTagElementRow> CollectElements(
        Document document,
        View activeView,
        IReadOnlyList<AutoTagCategoryOption> categories,
        AutoTagExistingTagIndex existingTagIndex,
        bool onlyUntagged,
        int maxPreviewCount)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(activeView, nameof(activeView));
        Guard.NotNull(categories, nameof(categories));
        Guard.NotNull(existingTagIndex, nameof(existingTagIndex));

        HashSet<long> selectedCategoryIds = categories
            .Where(category => category.IsSelected)
            .Select(category => category.CategoryIdValue)
            .ToHashSet();

        if (selectedCategoryIds.Count == 0)
        {
            return [];
        }

        int limit = Clamp(maxPreviewCount <= 0 ? 500 : maxPreviewCount, 50, 5000);
        List<AutoTagElementRow> rows = [];
        foreach (Element element in CollectVisibleElements(document, activeView))
        {
            Category? category = element.Category;
            if (!CanUseCategory(category) || !selectedCategoryIds.Contains(RevitElementIds.GetValue(category.Id)))
            {
                continue;
            }

            int existingTagCount = existingTagIndex.GetTagCount(element.Id);
            bool canApply = true;
            string status = AutoTagStatuses.Ready;
            string message = "Готово к постановке марки.";

            if (onlyUntagged && existingTagCount > 0)
            {
                canApply = false;
                status = AutoTagStatuses.Skipped;
                message = "У элемента уже есть марка на активном виде.";
            }
            else if (!placementService.TryGetTagPoint(element, activeView, out _, out string pointMessage))
            {
                canApply = false;
                status = AutoTagStatuses.Skipped;
                message = pointMessage;
            }
            else if (existingTagCount > 0)
            {
                message = "У элемента уже есть марка; фильтр выключен, можно создать дополнительную.";
            }

            rows.Add(new AutoTagElementRow(
                RevitElementIds.GetValue(element.Id),
                RevitElementIds.GetValue(category.Id),
                category.Name,
                GetElementName(element),
                existingTagCount,
                status,
                message,
                canApply));

            if (rows.Count >= limit)
            {
                break;
            }
        }

        return rows;
    }

    private static IEnumerable<Element> CollectVisibleElements(Document document, View activeView)
    {
        return new FilteredElementCollector(document, activeView.Id)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(element => element.Category is not null && element is not IndependentTag);
    }

    private static bool CanUseCategory(Category? category)
    {
        return category is not null
            && category.Id != ElementId.InvalidElementId
            && category.CategoryType == CategoryType.Model
            && category.AllowsBoundParameters;
    }

    private static bool IsTagCategory(Category? category)
    {
        if (category is null || category.Id == ElementId.InvalidElementId)
        {
            return false;
        }

        long categoryId = RevitElementIds.GetValue(category.Id);
        if (categoryId is < int.MinValue or > int.MaxValue)
        {
            return false;
        }

        string builtInCategoryName = ((BuiltInCategory)(int)categoryId).ToString();
        return builtInCategoryName.EndsWith("Tags", StringComparison.Ordinal)
            || builtInCategoryName.EndsWith("Tag", StringComparison.Ordinal);
    }

    private static string GetElementName(Element element)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(element.Name))
            {
                return element.Name;
            }
        }
        catch (Exception)
        {
        }

        if (element is FamilyInstance familyInstance)
        {
            try
            {
                string? symbolName = familyInstance.Symbol?.Name;
                if (!string.IsNullOrWhiteSpace(symbolName))
                {
                    return symbolName!;
                }
            }
            catch (Exception)
            {
            }
        }

        return element.GetType().Name;
    }

    private sealed record CategoryBucket(ElementId CategoryId, string Name)
    {
        public int ElementCount { get; set; }
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
