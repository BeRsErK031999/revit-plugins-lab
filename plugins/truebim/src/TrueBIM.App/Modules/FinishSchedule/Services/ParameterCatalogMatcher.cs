using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class ParameterCatalogMatcher
{
    public ParameterCompatibilityResult Evaluate(
        ParameterCatalogItem item,
        ParameterCatalogRequirement requirement)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (requirement is null)
        {
            throw new ArgumentNullException(nameof(requirement));
        }

        List<ParameterCompatibilityIssue> issues = [];
        if (item.Reference.BindingKind != requirement.BindingKind)
        {
            issues.Add(new ParameterCompatibilityIssue(
                "binding",
                $"{requirement.RoleName}: требуется параметр {FormatBinding(requirement.BindingKind)}."));
        }

        if (requirement.AllowedStorageKinds.Count > 0
            && !requirement.AllowedStorageKinds.Contains(item.Reference.StorageKind))
        {
            string allowed = string.Join(
                ", ",
                requirement.AllowedStorageKinds.Select(FormatStorage));
            issues.Add(new ParameterCompatibilityIssue(
                "storage",
                $"{requirement.RoleName}: допустимый тип значения — {allowed}; найден {FormatStorage(item.Reference.StorageKind)}."));
        }

        ParameterCategoryReference[] missingCategories = requirement.RequiredCategories
            .Where(category => !item.SupportsCategory(category.Id))
            .ToArray();
        if (missingCategories.Length > 0)
        {
            issues.Add(new ParameterCompatibilityIssue(
                "category",
                $"{requirement.RoleName}: параметр отсутствует у категорий {string.Join(", ", missingCategories.Select(category => category.Name))}."));
        }

        if (item.SampleCount == 0)
        {
            issues.Add(new ParameterCompatibilityIssue(
                "samples",
                $"{requirement.RoleName}: в документе нет элементов для проверки параметра."));
        }
        else if (requirement.RequireWritable && !item.IsWritableForAllSamples)
        {
            issues.Add(new ParameterCompatibilityIssue(
                "read_only",
                $"{requirement.RoleName}: параметр недоступен для записи у {item.ReadOnlySampleCount} из {item.SampleCount} проверенных элементов."));
        }

        return new ParameterCompatibilityResult(issues);
    }

    public IReadOnlyList<ParameterCatalogItem> FindCompatible(
        ParameterCatalog catalog,
        ParameterCatalogRequirement requirement)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        return catalog.Items
            .Where(item => Evaluate(item, requirement).IsCompatible)
            .ToArray();
    }

    private static string FormatBinding(ParameterBindingKind bindingKind)
    {
        return bindingKind == ParameterBindingKind.Type ? "типа" : "экземпляра";
    }

    private static string FormatStorage(ParameterStorageKind storageKind)
    {
        return storageKind switch
        {
            ParameterStorageKind.String => "Текст",
            ParameterStorageKind.Integer => "Целое / Да-Нет",
            ParameterStorageKind.Double => "Число",
            ParameterStorageKind.ElementId => "ElementId",
            ParameterStorageKind.None => "Без значения",
            _ => storageKind.ToString()
        };
    }
}
