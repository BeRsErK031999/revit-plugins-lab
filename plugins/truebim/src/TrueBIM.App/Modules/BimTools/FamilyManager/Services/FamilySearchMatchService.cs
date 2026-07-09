using TrueBIM.App.Modules.BimTools.FamilyManager.Models;

namespace TrueBIM.App.Modules.BimTools.FamilyManager.Services;

public sealed class FamilySearchMatchService
{
    public bool Matches(FamilyFileItem family, string searchText)
    {
        return string.IsNullOrWhiteSpace(searchText)
            || !string.IsNullOrWhiteSpace(FindMatchText(family, searchText));
    }

    public string FindMatchText(FamilyFileItem family, string searchText)
    {
        if (family is null || string.IsNullOrWhiteSpace(searchText))
        {
            return string.Empty;
        }

        string search = searchText.Trim();
        if (Contains(family.Name, search))
        {
            return $"Имя: {family.Name}";
        }

        if (Contains(family.Category, search))
        {
            return $"Категория: {family.Category}";
        }

        foreach (FamilyTypeInfo type in family.CachedTypes)
        {
            if (Contains(type.Name, search))
            {
                return $"Тип: {type.Name}";
            }

            foreach (FamilyTypeParameterInfo parameter in type.Parameters)
            {
                string parameterPrefix = string.IsNullOrWhiteSpace(type.Name)
                    ? parameter.Name
                    : $"{type.Name}: {parameter.Name}";
                if (Contains(parameter.Name, search))
                {
                    return $"Параметр: {parameterPrefix}";
                }

                if (Contains(parameter.ValueDisplay, search))
                {
                    return $"Значение: {parameterPrefix} = {parameter.ValueDisplay}";
                }

                if (Contains(parameter.FormulaDisplay, search))
                {
                    return $"Формула: {parameterPrefix} = {parameter.FormulaDisplay}";
                }
            }
        }

        string? catalogType = family.TypeCatalogTypeNames.FirstOrDefault(typeName => Contains(typeName, search));
        if (!string.IsNullOrWhiteSpace(catalogType))
        {
            return $"Тип catalog: {catalogType}";
        }

        if (Contains(family.TypeCatalogPath, search))
        {
            return $"Catalog: {family.TypeCatalogPath}";
        }

        if (Contains(family.FilePath, search))
        {
            return $"Файл: {family.FilePath}";
        }

        return string.Empty;
    }

    private static bool Contains(string? source, string search)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source!.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
