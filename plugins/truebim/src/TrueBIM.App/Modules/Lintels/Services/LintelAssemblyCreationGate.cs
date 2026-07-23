using TrueBIM.App.Modules.Lintels.Models;

namespace TrueBIM.App.Modules.Lintels.Services;

public static class LintelAssemblyCreationGate
{
    public static bool CanStart(IReadOnlyCollection<long> currentTypeIds)
    {
        if (currentTypeIds is null)
        {
            throw new ArgumentNullException(nameof(currentTypeIds));
        }

        return currentTypeIds.Count > 0;
    }

    public static bool IsCurrentSelection(
        IReadOnlyCollection<long> checkedTypeIds,
        IReadOnlyCollection<long> currentTypeIds)
    {
        if (checkedTypeIds is null)
        {
            throw new ArgumentNullException(nameof(checkedTypeIds));
        }

        if (currentTypeIds is null)
        {
            throw new ArgumentNullException(nameof(currentTypeIds));
        }

        return checkedTypeIds.Count == currentTypeIds.Count
            && checkedTypeIds.OrderBy(typeId => typeId)
                .SequenceEqual(currentTypeIds.OrderBy(typeId => typeId));
    }

    public static bool CanCreateOrFormatViews(
        IReadOnlyCollection<LintelTypeDiagnostic> selectedTypes)
    {
        if (selectedTypes is null)
        {
            throw new ArgumentNullException(nameof(selectedTypes));
        }

        return selectedTypes.Count > 0
            && selectedTypes.All(type => type.HasExistingAssembly);
    }
}
