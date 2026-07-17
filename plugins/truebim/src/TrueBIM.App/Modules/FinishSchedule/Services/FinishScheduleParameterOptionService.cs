using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishScheduleParameterOptionService
{
    private static readonly ParameterStorageKind[] TextStorage = [ParameterStorageKind.String];
    private static readonly ParameterStorageKind[] SectionStorage =
    [
        ParameterStorageKind.String,
        ParameterStorageKind.Integer,
        ParameterStorageKind.Double,
        ParameterStorageKind.ElementId
    ];

    private readonly ParameterCatalogMatcher matcher;

    public FinishScheduleParameterOptionService(ParameterCatalogMatcher matcher)
    {
        this.matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    }

    public IReadOnlyList<FinishScheduleParameterOption> GetDescriptionOptions(
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories,
        bool includeWalls,
        bool includeFloors,
        bool includeCeilings)
    {
        List<ParameterCategoryReference> requiredCategories = [];
        if (includeWalls)
        {
            requiredCategories.Add(categories.Walls);
        }

        if (includeFloors || includeCeilings)
        {
            requiredCategories.Add(categories.Floors);
        }

        if (requiredCategories.Count == 0)
        {
            return [];
        }

        return GetOptions(
            catalog,
            new ParameterCatalogRequirement(
                "Источник описания отделки",
                ParameterBindingKind.Type,
                TextStorage,
                requiredCategories,
                requireWritable: false));
    }

    public IReadOnlyList<FinishScheduleParameterOption> GetRoomIdentifierOptions(
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories)
    {
        return GetOptions(
            catalog,
            new ParameterCatalogRequirement(
                "Идентификатор помещения",
                ParameterBindingKind.Instance,
                TextStorage,
                [categories.Rooms],
                requireWritable: false));
    }

    public IReadOnlyList<FinishScheduleParameterOption> GetRoomOutputOptions(
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories)
    {
        return GetOptions(
            catalog,
            new ParameterCatalogRequirement(
                "Выходной параметр помещения",
                ParameterBindingKind.Instance,
                TextStorage,
                [categories.Rooms],
                requireWritable: true));
    }

    public IReadOnlyList<FinishScheduleParameterOption> GetOwnershipOptions(
        ParameterCatalog catalog,
        ParameterCategoryReference category)
    {
        return GetOptions(
            catalog,
            new ParameterCatalogRequirement(
                "Принадлежность помещению",
                ParameterBindingKind.Instance,
                TextStorage,
                [category],
                requireWritable: true));
    }

    public IReadOnlyList<FinishScheduleParameterOption> GetSectionOptions(
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories)
    {
        return GetOptions(
            catalog,
            new ParameterCatalogRequirement(
                "Параметр секции или корпуса",
                ParameterBindingKind.Instance,
                SectionStorage,
                [categories.Rooms],
                requireWritable: false));
    }

    private IReadOnlyList<FinishScheduleParameterOption> GetOptions(
        ParameterCatalog catalog,
        ParameterCatalogRequirement requirement)
    {
        return matcher.FindCompatible(catalog, requirement)
            .Select(item => new FinishScheduleParameterOption(
                item.Reference,
                CreateDisplayName(item.Reference)))
            .OrderBy(option => option.Reference.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(option => option.Reference.StableKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static string CreateDisplayName(ParameterReference reference)
    {
        string identity = reference.IdentityKind switch
        {
            ParameterIdentityKind.BuiltIn => "системный",
            ParameterIdentityKind.Shared => $"общий · {reference.SharedParameterGuid!.Value.ToString("N").Substring(0, 8)}",
            ParameterIdentityKind.Project => $"проектный · id {reference.DefinitionElementId}",
            _ => reference.IdentityKind.ToString()
        };

        return $"{reference.Name} — {identity}";
    }
}
