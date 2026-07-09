using Autodesk.Revit.DB;
using TrueBIM.App.Modules.ViewVisibility.Models;
using TrueBIM.App.Services;
using TrueBIM.App.Services.Logging;

namespace TrueBIM.App.Modules.ViewVisibility.Services;

public sealed class ViewCategoryVisibilityService
{
    private readonly ITrueBimLogger logger;

    public ViewCategoryVisibilityService(ITrueBimLogger logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ViewCategoryVisibilityItem> Collect(Document document, View view)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(view, nameof(view));

        List<ViewCategoryVisibilityItem> items = new();
        foreach (Category category in document.Settings.Categories.Cast<Category>())
        {
            if (TryCreateItem(view, category, out ViewCategoryVisibilityItem? item) && item is not null)
            {
                items.Add(item);
            }
        }

        return items
            .OrderBy(item => GetCategoryTypeOrder(item.CategoryType))
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool TryCollect(
        Document document,
        View view,
        BuiltInCategory builtInCategory,
        out ViewCategoryVisibilityItem? item)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(view, nameof(view));

        item = null;
        Category? category = Category.GetCategory(document, builtInCategory);
        return category is not null && TryCreateItem(view, category, out item);
    }

    public ViewCategoryVisibilityApplyResult Apply(Document document, View view, IReadOnlyList<ViewCategoryVisibilityUpdate> updates)
    {
        Guard.NotNull(document, nameof(document));
        Guard.NotNull(view, nameof(view));
        Guard.NotNull(updates, nameof(updates));

        int shownCount = 0;
        int hiddenCount = 0;
        int unchangedCount = 0;
        int skippedCount = 0;

        using Transaction transaction = new(document, "TrueBIM: видимость категорий");
        transaction.Start();

        try
        {
            foreach (ViewCategoryVisibilityUpdate update in updates)
            {
                Category? category = Category.GetCategory(document, update.CategoryId);
                if (category is null || !CanControlCategory(view, category))
                {
                    skippedCount++;
                    continue;
                }

                bool shouldHide = !update.IsVisible;
                bool isHidden = view.GetCategoryHidden(update.CategoryId);
                if (isHidden == shouldHide)
                {
                    unchangedCount++;
                    continue;
                }

                view.SetCategoryHidden(update.CategoryId, shouldHide);
                if (shouldHide)
                {
                    hiddenCount++;
                }
                else
                {
                    shownCount++;
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.RollBack();
            throw;
        }

        logger.Info(
            $"Applied category visibility for view '{view.Name}'. Shown: {shownCount}; hidden: {hiddenCount}; unchanged: {unchangedCount}; skipped: {skippedCount}.");

        return new ViewCategoryVisibilityApplyResult(shownCount, hiddenCount, unchangedCount, skippedCount);
    }

    private static bool CanControlCategory(View view, Category category)
    {
        if (category.Id == ElementId.InvalidElementId || string.IsNullOrWhiteSpace(category.Name))
        {
            return false;
        }

        if (category.CategoryType is CategoryType.Invalid or CategoryType.Internal)
        {
            return false;
        }

        try
        {
            return view.CanCategoryBeHidden(category.Id);
        }
        catch (Exception exception) when (IsExpectedRevitCategoryException(exception))
        {
            return false;
        }
    }

    private bool TryCreateItem(View view, Category category, out ViewCategoryVisibilityItem? item)
    {
        item = null;
        if (!CanControlCategory(view, category))
        {
            return false;
        }

        bool isVisible;
        try
        {
            isVisible = !view.GetCategoryHidden(category.Id);
        }
        catch (Exception exception) when (IsExpectedRevitCategoryException(exception))
        {
            logger.Warning($"Skipping category '{category.Name}' because its visibility state cannot be read.");
            return false;
        }

        item = new ViewCategoryVisibilityItem(
            category.Id,
            category.Name,
            category.CategoryType,
            isVisible);
        return true;
    }

    private static int GetCategoryTypeOrder(CategoryType categoryType)
    {
        return categoryType switch
        {
            CategoryType.Model => 0,
            CategoryType.Annotation => 1,
            CategoryType.AnalyticalModel => 2,
            _ => 3
        };
    }

    private static bool IsExpectedRevitCategoryException(Exception exception)
    {
        return exception is Autodesk.Revit.Exceptions.ApplicationException
            or Autodesk.Revit.Exceptions.ArgumentException
            or ArgumentException
            or InvalidOperationException;
    }
}
