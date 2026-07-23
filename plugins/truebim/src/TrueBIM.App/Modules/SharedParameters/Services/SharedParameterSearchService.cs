using TrueBIM.App.Modules.SharedParameters.Models;

namespace TrueBIM.App.Modules.SharedParameters.Services;

public sealed class SharedParameterSearchService
{
    public bool IsGuidInputValid(string? input)
    {
        return Guid.TryParse(input, out _);
    }

    public bool Matches(SharedParameterDescriptor parameter, string? query)
    {
        if (parameter is null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }

        string normalizedQuery = Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            return true;
        }

        string normalizedName = Normalize(parameter.Name);
        string guidWithSeparators = parameter.Guid.ToString("D");
        string guidWithoutSeparators = parameter.Guid.ToString("N");
        return normalizedName.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0
            || guidWithSeparators.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0
            || guidWithoutSeparators.IndexOf(
                RemoveGuidSeparators(normalizedQuery),
                StringComparison.Ordinal) >= 0;
    }

    public IReadOnlyList<SharedParameterDescriptor> Filter(
        IReadOnlyList<SharedParameterDescriptor> parameters,
        string? query,
        SharedParameterListFilter filter,
        IReadOnlyDictionary<string, SharedParameterProjectAnalysis>? analyses = null)
    {
        if (parameters is null)
        {
            throw new ArgumentNullException(nameof(parameters));
        }

        return parameters
            .Where(parameter => Matches(parameter, query))
            .Where(parameter => MatchesFilter(parameter, filter, analyses))
            .OrderBy(parameter => parameter.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(parameter => parameter.Guid)
            .ToList();
    }

    public bool RequiresAnalysis(SharedParameterListFilter filter)
    {
        return filter is SharedParameterListFilter.UsedInSchedules
            or SharedParameterListFilter.UsedInViewFilters
            or SharedParameterListFilter.PresentInFamilies
            or SharedParameterListFilter.Unused;
    }

    private static bool MatchesFilter(
        SharedParameterDescriptor parameter,
        SharedParameterListFilter filter,
        IReadOnlyDictionary<string, SharedParameterProjectAnalysis>? analyses)
    {
        if (filter == SharedParameterListFilter.All)
        {
            return true;
        }

        if (filter == SharedParameterListFilter.Instance)
        {
            return parameter.BindingKind == SharedParameterBindingKind.Instance;
        }

        if (filter == SharedParameterListFilter.Type)
        {
            return parameter.BindingKind == SharedParameterBindingKind.Type;
        }

        if (filter == SharedParameterListFilter.Bound)
        {
            return parameter.HasProjectBinding;
        }

        if (filter == SharedParameterListFilter.Unbound)
        {
            return !parameter.HasProjectBinding;
        }

        if (analyses is null || !analyses.TryGetValue(parameter.IdentityKey, out SharedParameterProjectAnalysis? analysis))
        {
            return false;
        }

        return filter switch
        {
            SharedParameterListFilter.UsedInSchedules => analysis.ScheduleFields.Count > 0,
            SharedParameterListFilter.UsedInViewFilters => analysis.ViewFilters.Count > 0,
            SharedParameterListFilter.PresentInFamilies => analysis.FamilyCountWithParameter > 0,
            SharedParameterListFilter.Unused => !analysis.HasAnyUsage,
            _ => true
        };
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Trim('{', '}')
            .ToLowerInvariant();
    }

    private static string RemoveGuidSeparators(string value)
    {
        return value.Replace("-", string.Empty);
    }
}
