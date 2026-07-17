using TrueBIM.App.Modules.FinishSchedule.Models;

namespace TrueBIM.App.Modules.FinishSchedule.Services;

public sealed class FinishSchedulePreferredParameterResolver
{
    private readonly FinishScheduleParameterOptionService optionService;

    public FinishSchedulePreferredParameterResolver(FinishScheduleParameterOptionService optionService)
    {
        this.optionService = optionService ?? throw new ArgumentNullException(nameof(optionService));
    }

    public FinishScheduleSettings Resolve(
        FinishScheduleSettings settings,
        ParameterCatalog catalog,
        FinishScheduleParameterCategories categories)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (categories is null)
        {
            throw new ArgumentNullException(nameof(categories));
        }

        IReadOnlyList<FinishScheduleParameterOption> descriptionOptions = optionService.GetDescriptionOptions(
            catalog,
            categories,
            settings.Walls.IsEnabled,
            settings.Floors.IsEnabled,
            settings.Ceilings.IsEnabled);
        IReadOnlyList<FinishScheduleParameterOption> roomOutputOptions = optionService.GetRoomOutputOptions(
            catalog,
            categories);

        return settings with
        {
            DescriptionParameter = ResolveReference(
                settings.DescriptionParameter,
                descriptionOptions,
                FinishSchedulePreferredParameterNames.Description),
            RoomListOutputParameter = ResolveReference(
                settings.RoomListOutputParameter,
                roomOutputOptions,
                FinishSchedulePreferredParameterNames.RoomListOutput),
            Walls = ResolveCategory(
                settings.Walls,
                roomOutputOptions,
                optionService.GetOwnershipOptions(catalog, categories.Walls),
                FinishSchedulePreferredParameterNames.WallsOwnership,
                FinishSchedulePreferredParameterNames.WallsDescription,
                FinishSchedulePreferredParameterNames.WallsArea,
                settings.WriteOwnership),
            Floors = ResolveCategory(
                settings.Floors,
                roomOutputOptions,
                optionService.GetOwnershipOptions(catalog, categories.Floors),
                FinishSchedulePreferredParameterNames.FloorsOwnership,
                FinishSchedulePreferredParameterNames.FloorsDescription,
                FinishSchedulePreferredParameterNames.FloorsArea,
                settings.WriteOwnership),
            Ceilings = ResolveCategory(
                settings.Ceilings,
                roomOutputOptions,
                optionService.GetOwnershipOptions(catalog, categories.Floors),
                FinishSchedulePreferredParameterNames.CeilingsOwnership,
                FinishSchedulePreferredParameterNames.CeilingsDescription,
                FinishSchedulePreferredParameterNames.CeilingsArea,
                settings.WriteOwnership)
        };
    }

    private static FinishCategorySettings ResolveCategory(
        FinishCategorySettings settings,
        IReadOnlyList<FinishScheduleParameterOption> roomOutputOptions,
        IReadOnlyList<FinishScheduleParameterOption> ownershipOptions,
        string preferredOwnershipName,
        string preferredDescriptionName,
        string preferredAreaName,
        bool writeOwnership)
    {
        if (!settings.IsEnabled)
        {
            return settings;
        }

        return settings with
        {
            OwnershipParameter = writeOwnership
                ? ResolveReference(settings.OwnershipParameter, ownershipOptions, preferredOwnershipName)
                : settings.OwnershipParameter,
            OutputDescriptionParameter = ResolveReference(
                settings.OutputDescriptionParameter,
                roomOutputOptions,
                preferredDescriptionName),
            OutputAreaParameter = ResolveReference(
                settings.OutputAreaParameter,
                roomOutputOptions,
                preferredAreaName)
        };
    }

    private static ParameterReference? ResolveReference(
        ParameterReference? current,
        IEnumerable<FinishScheduleParameterOption> options,
        string preferredName)
    {
        if (current is not null)
        {
            return current;
        }

        FinishScheduleParameterOption[] matches = options
            .Where(option => string.Equals(
                option.Reference.Name,
                preferredName,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0].Reference : null;
    }
}
