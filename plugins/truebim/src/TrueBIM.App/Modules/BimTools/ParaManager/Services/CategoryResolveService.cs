using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.ParaManager.Services;

public sealed class CategoryResolveService
{
    private static readonly IReadOnlyDictionary<string, BuiltInCategory> Aliases =
        new Dictionary<string, BuiltInCategory>(StringComparer.CurrentCultureIgnoreCase)
        {
            ["Rooms"] = BuiltInCategory.OST_Rooms,
            ["Room"] = BuiltInCategory.OST_Rooms,
            ["Помещения"] = BuiltInCategory.OST_Rooms,
            ["Помещение"] = BuiltInCategory.OST_Rooms,
            ["Walls"] = BuiltInCategory.OST_Walls,
            ["Wall"] = BuiltInCategory.OST_Walls,
            ["Стены"] = BuiltInCategory.OST_Walls,
            ["Стена"] = BuiltInCategory.OST_Walls,
            ["Doors"] = BuiltInCategory.OST_Doors,
            ["Door"] = BuiltInCategory.OST_Doors,
            ["Двери"] = BuiltInCategory.OST_Doors,
            ["Дверь"] = BuiltInCategory.OST_Doors,
            ["Windows"] = BuiltInCategory.OST_Windows,
            ["Window"] = BuiltInCategory.OST_Windows,
            ["Окна"] = BuiltInCategory.OST_Windows,
            ["Окно"] = BuiltInCategory.OST_Windows,
            ["Floors"] = BuiltInCategory.OST_Floors,
            ["Floor"] = BuiltInCategory.OST_Floors,
            ["Перекрытия"] = BuiltInCategory.OST_Floors,
            ["Полы"] = BuiltInCategory.OST_Floors,
            ["Ceilings"] = BuiltInCategory.OST_Ceilings,
            ["Ceiling"] = BuiltInCategory.OST_Ceilings,
            ["Потолки"] = BuiltInCategory.OST_Ceilings,
            ["Roofs"] = BuiltInCategory.OST_Roofs,
            ["Roof"] = BuiltInCategory.OST_Roofs,
            ["Крыши"] = BuiltInCategory.OST_Roofs,
            ["Generic Models"] = BuiltInCategory.OST_GenericModel,
            ["GenericModels"] = BuiltInCategory.OST_GenericModel,
            ["Обобщенные модели"] = BuiltInCategory.OST_GenericModel,
            ["Обобщённые модели"] = BuiltInCategory.OST_GenericModel,
            ["Furniture"] = BuiltInCategory.OST_Furniture,
            ["Мебель"] = BuiltInCategory.OST_Furniture,
            ["Mechanical Equipment"] = BuiltInCategory.OST_MechanicalEquipment,
            ["MechanicalEquipment"] = BuiltInCategory.OST_MechanicalEquipment,
            ["Оборудование"] = BuiltInCategory.OST_MechanicalEquipment,
            ["Ducts"] = BuiltInCategory.OST_DuctCurves,
            ["Воздуховоды"] = BuiltInCategory.OST_DuctCurves,
            ["Pipes"] = BuiltInCategory.OST_PipeCurves,
            ["Трубы"] = BuiltInCategory.OST_PipeCurves,
            ["Cable Trays"] = BuiltInCategory.OST_CableTray,
            ["CableTrays"] = BuiltInCategory.OST_CableTray,
            ["Кабельные лотки"] = BuiltInCategory.OST_CableTray,
            ["Conduits"] = BuiltInCategory.OST_Conduit,
            ["Короба"] = BuiltInCategory.OST_Conduit,
            ["Structural Columns"] = BuiltInCategory.OST_StructuralColumns,
            ["StructuralColumns"] = BuiltInCategory.OST_StructuralColumns,
            ["Несущие колонны"] = BuiltInCategory.OST_StructuralColumns
        };

    public bool CategoryExists(Document document, string categoryName)
    {
        return ResolveCategory(document, categoryName) is not null;
    }

    public IReadOnlyList<string> CollectBindableCategoryNames(Document document)
    {
        return document.Settings.Categories
            .Cast<Category>()
            .Where(IsUsableCategory)
            .Select(category => category.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<Category> ResolveCategories(Document document, IEnumerable<string> categoryNames, out IReadOnlyList<string> missing)
    {
        List<Category> categories = [];
        List<string> missingCategories = [];
        HashSet<string> seen = new(StringComparer.CurrentCultureIgnoreCase);

        foreach (string categoryName in categoryNames)
        {
            Category? category = ResolveCategory(document, categoryName);
            if (category is null)
            {
                missingCategories.Add(categoryName);
                continue;
            }

            if (seen.Add(category.Name))
            {
                categories.Add(category);
            }
        }

        missing = missingCategories;
        return categories;
    }

    private static Category? ResolveCategory(Document document, string categoryName)
    {
        string normalized = categoryName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        Category? category = document.Settings.Categories
            .Cast<Category>()
            .FirstOrDefault(candidate => IsUsableCategory(candidate)
                && string.Equals(candidate.Name, normalized, StringComparison.CurrentCultureIgnoreCase));
        if (category is not null)
        {
            return category;
        }

        if (Aliases.TryGetValue(normalized, out BuiltInCategory builtInCategory))
        {
            Category? aliasCategory = Category.GetCategory(document, builtInCategory);
            return IsUsableCategory(aliasCategory) ? aliasCategory : null;
        }

        return null;
    }

    private static bool IsUsableCategory(Category? category)
    {
        return category is not null
            && category.AllowsBoundParameters
            && category.CategoryType != CategoryType.Internal;
    }
}
