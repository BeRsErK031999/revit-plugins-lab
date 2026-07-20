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
                optionService.GetOwnershipOptions(catalog, categories.Ceilings),
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
        FinishScheduleParameterOption[] available = options.ToArray();
        if (current is not null)
        {
            FinishScheduleParameterOption? exact = available.FirstOrDefault(
                option => option.Reference.StableKey == current.StableKey);
            if (exact is not null)
            {
                return exact.Reference;
            }

            FinishScheduleParameterOption[] portableMatches = available
                .Where(option => IsPortableMatch(current, option.Reference))
                .Take(2)
                .ToArray();
            if (portableMatches.Length == 1)
            {
                return portableMatches[0].Reference;
            }
        }

        FinishScheduleParameterOption[] matches = available
            .Where(option => string.Equals(
                option.Reference.Name,
                preferredName,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0].Reference : null;
    }

    private static bool IsPortableMatch(ParameterReference source, ParameterReference candidate)
    {
        if (source.IdentityKind == ParameterIdentityKind.Shared
            && candidate.IdentityKind == ParameterIdentityKind.Shared
            && source.SharedParameterGuid == candidate.SharedParameterGuid)
        {
            return true;
        }

        if (source.IdentityKind == ParameterIdentityKind.BuiltIn
            && candidate.IdentityKind == ParameterIdentityKind.BuiltIn
            && source.BuiltInParameterId == candidate.BuiltInParameterId)
        {
            return true;
        }

        return string.Equals(source.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
            && source.BindingKind == candidate.BindingKind
            && source.StorageKind == candidate.StorageKind;
    }
}
