using TrueBIM.App.Modules.Lintels.Models;

namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelExistingAssemblyMatcher
{
    public static LintelDiagnosticResult Apply(
        LintelDiagnosticResult result,
        IEnumerable<string> existingAssemblyNames)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (existingAssemblyNames is null)
        {
            throw new ArgumentNullException(nameof(existingAssemblyNames));
        }

        HashSet<string> existingNames = existingAssemblyNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        LintelTypeDiagnostic[] types = result.Types
            .Select(type => AttachExistingAssembly(type, existingNames))
            .ToArray();
        return result with { Types = types };
    }

    private static LintelTypeDiagnostic AttachExistingAssembly(
        LintelTypeDiagnostic type,
        ISet<string> existingAssemblyNames)
    {
        string assemblyName = LintelArtifactNameBuilder.Build(type).AssemblyName;
        return type with
        {
            ExistingAssemblyName = existingAssemblyNames.Contains(assemblyName)
                ? assemblyName
                : null
        };
    }
}
