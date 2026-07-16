using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using TrueBIM.App.Modules.BimTools.ScheduleImport.Models;
using TrueBIM.App.Services;

namespace TrueBIM.App.Modules.BimTools.ScheduleImport.Services;

public sealed class ScheduleImportContextService
{
    public ScheduleImportContext Create(UIDocument uiDocument)
    {
        Guard.NotNull(uiDocument, nameof(uiDocument));

        Document document = uiDocument.Document;
        View activeView = document.ActiveView;
        List<string> warnings = [];
        IReadOnlyList<ScheduleCategoryOption> categories = CollectScheduleCategories(document, warnings);

        return new ScheduleImportContext(
            string.IsNullOrWhiteSpace(document.Title) ? "Документ Revit" : document.Title,
            string.IsNullOrWhiteSpace(activeView.Name) ? "Активный вид" : activeView.Name,
            activeView.ViewType.ToString(),
            RevitElementIds.GetValue(activeView.Id),
            categories,
            warnings);
    }

    private static IReadOnlyList<ScheduleCategoryOption> CollectScheduleCategories(
        Document document,
        List<string> warnings)
    {
        try
        {
            return ViewSchedule.GetValidCategoriesForSchedule()
                .Select(categoryId => Category.GetCategory(document, categoryId))
                .Where(category => category is not null && !string.IsNullOrWhiteSpace(category.Name))
                .Select(category => new ScheduleCategoryOption(
                    RevitElementIds.GetValue(category!.Id),
                    category.Name.Trim()))
                .GroupBy(category => category.CategoryId)
                .Select(group => group.First())
                .OrderBy(category => category.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception exception)
        {
            warnings.Add($"Не удалось прочитать категории спецификаций Revit: {exception.Message}");
            return Array.Empty<ScheduleCategoryOption>();
        }
    }
}
